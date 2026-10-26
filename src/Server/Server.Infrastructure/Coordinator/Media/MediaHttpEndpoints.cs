using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Server.Application.Coordinator.Media;
using Server.Core.Models.Media;
using Shared.Models.Media;
using Shared.Validation;
using tusdotnet;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;
using tusdotnet.Models.Expiration;

namespace Server.Infrastructure.Coordinator.Media;

public interface IMediaHttpEndpoints
{
    void Map(IEndpointRouteBuilder endpoints);
}

public sealed class MediaHttpEndpoints : IMediaHttpEndpoints
{
    private const string UserIdItem = "media.userId";
    private const string UploadItem = "media.upload";
    private const string BearerPrefix = "Bearer ";

    private readonly ILogger _logger;
    private readonly IMediaService _media;
    private readonly TusDiskMediaStorage _storage;

    public MediaHttpEndpoints(ILogger logger, IMediaService media, TusDiskMediaStorage storage)
    {
        _logger = logger;
        _media = media;
        _storage = storage;
    }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapTus(MediaEndpoints.Uploads, _ => Task.FromResult(CreateTusConfiguration()));
        endpoints.MapGet(MediaEndpoints.Images + "/{id:int}", (RequestDelegate)GetImageAsync);
        endpoints.MapGet(MediaEndpoints.Files + "/{id:int}", (RequestDelegate)GetFileAsync);
        endpoints.MapGet(MediaEndpoints.Avatars + "/{id}", (RequestDelegate)GetAvatarAsync);
    }

    private DefaultTusConfiguration CreateTusConfiguration() => new()
    {
        Store = _storage.Store,
        MaxAllowedUploadSizeInBytesLong = _media.MaxFileSizeBytes,
        Expiration = new AbsoluteExpiration(_media.UnattachedLifetime),
        Events = new Events
        {
            OnAuthorizeAsync = AuthorizeAsync,
            OnBeforeCreateAsync = BeforeCreateAsync,
            OnCreateCompleteAsync = CreateCompleteAsync,
            OnFileCompleteAsync = FileCompleteAsync,
            OnDeleteCompleteAsync = context => _media.ForgetUploadAsync(context.FileId, context.CancellationToken)
        }
    };

    private async Task AuthorizeAsync(AuthorizeContext context)
    {
        if (!TryAuthorize(context.HttpContext, out var userId))
        {
            context.FailRequest(HttpStatusCode.Unauthorized);
            return;
        }

        var isUpload = context.Intent is IntentType.CreateFile or IntentType.WriteFile;
        if (!await _media.IsAllowedAsync(userId, isUpload, context.CancellationToken))
        {
            context.FailRequest(HttpStatusCode.Forbidden);
            return;
        }

        context.HttpContext.Items[UserIdItem] = userId;

        var allowed = context.Intent switch
        {
            IntentType.CreateFile or IntentType.GetOptions => true,
            IntentType.GetFileInfo => await _media.CanReadUploadAsync(context.FileId, userId, context.CancellationToken),
            IntentType.WriteFile => await _media.CanWriteUploadAsync(context.FileId, userId, context.CancellationToken),
            IntentType.DeleteFile => await _media.CanDeleteUploadAsync(context.FileId, userId, context.CancellationToken),
            _ => false
        };

        if (!allowed)
            context.FailRequest(HttpStatusCode.Forbidden);
    }

    private async Task BeforeCreateAsync(BeforeCreateContext context)
    {
        if (context.UploadLengthIsDeferred || context.FileConcatenation != null
            || !TryReadUpload(context.Metadata, context.UploadLength, out var upload))
        {
            context.FailRequest(HttpStatusCode.BadRequest, "The upload metadata is not valid.");
            return;
        }

        var userId = (int)context.HttpContext.Items[UserIdItem]!;
        var rejection = await _media.CheckNewUploadAsync(userId, upload, context.CancellationToken);
        if (rejection is { } reason)
        {
            context.FailRequest(StatusOf(reason), reason.ToString());
            return;
        }

        context.HttpContext.Items[UploadItem] = upload;
    }

    private async Task CreateCompleteAsync(CreateCompleteContext context)
    {
        var userId = (int)context.HttpContext.Items[UserIdItem]!;
        var upload = (NewMediaUpload)context.HttpContext.Items[UploadItem]!;

        try
        {
            await _media.RegisterUploadAsync(context.FileId, userId, upload, context.CancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Registering upload {UploadId} of user {UserId} failed.", context.FileId, userId);
            await _storage.DeleteAsync(context.FileId, CancellationToken.None);
            throw;
        }
    }

    private Task FileCompleteAsync(FileCompleteContext context) =>
        _media.CompleteUploadAsync(context.FileId, context.CancellationToken);

    private async Task GetImageAsync(HttpContext context)
    {
        if (!await TryAuthorizeReadAsync(context))
            return;

        var variant = context.Request.Query[MediaEndpoints.VariantParameter].ToString();
        if (variant is not (MediaEndpoints.VariantOriginal or MediaEndpoints.VariantPreview)
            || !int.TryParse(context.Request.RouteValues["id"]?.ToString(), NumberStyles.None,
                CultureInfo.InvariantCulture, out var imageId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var file = await _media.OpenImageAsync(imageId, variant == MediaEndpoints.VariantPreview, context.RequestAborted);
        if (file == null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await SendFileAsync(context, file);
    }

    private async Task GetFileAsync(HttpContext context)
    {
        if (!await TryAuthorizeReadAsync(context))
            return;

        if (!int.TryParse(context.Request.RouteValues["id"]?.ToString(), NumberStyles.None,
                CultureInfo.InvariantCulture, out var fileId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var file = await _media.OpenFileAsync(fileId, context.RequestAborted);
        if (file == null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await SendFileAsync(context, file);
    }

    private async Task GetAvatarAsync(HttpContext context)
    {
        if (!await TryAuthorizeReadAsync(context))
            return;

        var uploadId = context.Request.RouteValues["id"]?.ToString();
        if (uploadId is not { Length: Limits.MediaUploadIdLength * 2 } || !uploadId.All(char.IsAsciiHexDigit))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var file = await _media.OpenAvatarAsync(uploadId.ToLowerInvariant(), context.RequestAborted);
        if (file == null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await SendFileAsync(context, file);
    }

    private static async Task SendFileAsync(HttpContext context, MediaFile file)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "private, max-age=31536000, immutable";
        context.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";

        // The type of a file is what its sender said: it is never served as something a browser would run
        if (file.FileName is { } fileName)
        {
            var disposition = new ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(fileName);
            context.Response.Headers[HeaderNames.ContentDisposition] = disposition.ToString();
            await Results.Stream(file.Content, "application/octet-stream", enableRangeProcessing: true)
                .ExecuteAsync(context);
            return;
        }

        await Results.Stream(file.Content, file.MimeType, enableRangeProcessing: true).ExecuteAsync(context);
    }

    /// <summary>Answers 401 or 403 itself when the request may not read.</summary>
    private async Task<bool> TryAuthorizeReadAsync(HttpContext context)
    {
        if (!TryAuthorize(context, out var userId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return false;
        }

        if (await _media.IsAllowedAsync(userId, isUpload: false, context.RequestAborted))
            return true;

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return false;
    }

    private bool TryAuthorize(HttpContext context, out int userId)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return _media.TryAuthorize(header[BearerPrefix.Length..].Trim(), out userId);

        userId = 0;
        return false;
    }

    private static bool TryReadUpload(Dictionary<string, Metadata> metadata, long length, out NewMediaUpload upload)
    {
        upload = null!;

        if (!TryReadString(metadata, MediaUploadMetadata.MimeType, out var mimeType)
            || mimeType.Length > Limits.MimeTypeMaxLength
            || !TryReadString(metadata, MediaUploadMetadata.Kind, out var kindText))
            return false;

        MediaUploadKind kind;
        switch (kindText)
        {
            case MediaUploadMetadata.KindImage:
                kind = MediaUploadKind.Image;
                break;
            case MediaUploadMetadata.KindImagePreview:
                kind = MediaUploadKind.ImagePreview;
                break;
            case MediaUploadMetadata.KindFile:
                kind = MediaUploadKind.File;
                break;
            case MediaUploadMetadata.KindAvatar:
                kind = MediaUploadKind.Avatar;
                break;
            default:
                return false;
        }

        int? chatId = null;
        if (kind == MediaUploadKind.Avatar)
        {
            if (metadata.ContainsKey(MediaUploadMetadata.ChatId))
                return false;
        }
        else
        {
            if (!TryReadString(metadata, MediaUploadMetadata.ChatId, out var chatIdText)
                || !int.TryParse(chatIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedChatId))
                return false;

            chatId = parsedChatId;
        }

        upload = new NewMediaUpload(chatId, kind, mimeType.ToLowerInvariant(), length);
        return true;
    }

    private static bool TryReadString(Dictionary<string, Metadata> metadata, string key, out string value)
    {
        value = string.Empty;
        if (!metadata.TryGetValue(key, out var entry) || entry.HasEmptyValue)
            return false;

        value = entry.GetString(Encoding.UTF8);
        return value.Length > 0;
    }

    private static HttpStatusCode StatusOf(MediaUploadRejection rejection) => rejection switch
    {
        MediaUploadRejection.ChatNotExists => HttpStatusCode.NotFound,
        MediaUploadRejection.AttachmentsNotAllowed => HttpStatusCode.Forbidden,
        MediaUploadRejection.TypeNotAllowed => HttpStatusCode.UnsupportedMediaType,
        MediaUploadRejection.TooLarge => HttpStatusCode.RequestEntityTooLarge,
        MediaUploadRejection.TooManyUploads => HttpStatusCode.TooManyRequests,
        MediaUploadRejection.QuotaExceeded => HttpStatusCode.InsufficientStorage,
        _ => HttpStatusCode.BadRequest
    };
}
