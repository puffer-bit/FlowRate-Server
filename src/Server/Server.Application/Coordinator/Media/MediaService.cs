using Shared.Enums;
using Server.Application.Coordinator.Roles;
using Microsoft.Extensions.Logging;
using Server.Application.Abstractions.Media;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Users;
using Server.Core.Models.Coordinator.Settings;
using Server.Core.Models.Media;
using Shared.Enums.User;
using Shared.Validation;

namespace Server.Application.Coordinator.Media;

public sealed class MediaService : IMediaService
{
    private static readonly TimeSpan CleanUpInterval = TimeSpan.FromHours(1);

    private readonly ILogger _logger;
    private readonly IMediaUploadRepository _uploads;
    private readonly IMediaStorage _storage;
    private readonly IChatSessionManager _chats;
    private readonly MediaTokenService _tokens;
    private volatile CoordinatorUploadSettings _uploadSettings;
    private volatile CoordinatorMediaSettings _limits;
    private readonly string _coordinatorInstanceId;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _stopping = new();
    // Without it the state of the users is not checked
    private readonly IUserManager? _users;
    private ICoordinatorPermissions? _permissions;

    private Task? _cleanUpLoop;

    public MediaService(
        ILogger logger,
        IMediaUploadRepository uploads,
        IMediaStorage storage,
        IChatSessionManager chats,
        CoordinatorUploadSettings uploadSettings,
        CoordinatorMediaSettings limits,
        string coordinatorInstanceId,
        TimeProvider? timeProvider = null,
        IUserManager? users = null)
    {
        _users = users;
        _logger = logger;
        _uploads = uploads;
        _storage = storage;
        _chats = chats;
        _uploadSettings = uploadSettings;
        _limits = limits;
        _coordinatorInstanceId = coordinatorInstanceId;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _tokens = new MediaTokenService(TimeSpan.FromMinutes(uploadSettings.TokenLifetimeMinutes), _timeProvider);
    }

    public long MaxFileSizeBytes => _limits.MaxFileSizeBytes;

    public TimeSpan UnattachedLifetime => TimeSpan.FromHours(_uploadSettings.UnattachedLifetimeHours);

    /// <summary>Starts checking uploads to chats against the permissions of the uploader there.</summary>
    public void Watch(ICoordinatorPermissions permissions) => _permissions = permissions;

    public void SetLimits(CoordinatorMediaSettings limits) => _limits = limits;

    public void SetUploadSettings(CoordinatorUploadSettings uploadSettings)
    {
        _uploadSettings = uploadSettings;
        // Tokens already issued keep the expiry written in them
        _tokens.Lifetime = TimeSpan.FromMinutes(uploadSettings.TokenLifetimeMinutes);
    }

    public void Start() => _cleanUpLoop ??= Task.Run(CleanUpLoopAsync);

    public (string Token, DateTime ExpiresAt) IssueToken(int userId) => _tokens.Issue(userId);

    public bool TryAuthorize(string? token, out int userId) => _tokens.TryValidate(token, out userId);

    public async Task<bool> IsAllowedAsync(int userId, bool isUpload, CancellationToken cancellationToken = default)
    {
        if (_users == null)
            return true;

        var user = await _users.GetUserAsync(userId);
        return user != null && (isUpload ? user.State == UserState.Normal : user.State != UserState.Banned);
    }

    public async Task<MediaUploadRejection?> CheckNewUploadAsync(int userId, NewMediaUpload upload,
        CancellationToken cancellationToken = default)
    {
        if (upload.Size <= 0)
            return MediaUploadRejection.InvalidMetadata;

        if (upload.Kind == MediaUploadKind.Avatar)
        {
            if (upload.ChatId != null)
                return MediaUploadRejection.InvalidMetadata;
        }
        else
        {
            if (upload.ChatId is not { } chatId)
                return MediaUploadRejection.InvalidMetadata;

            if (!_chats.TryGetSession(chatId, out var chat))
                return MediaUploadRejection.ChatNotExists;

            if (!chat.CanPost || !chat.Options.IsAttachmentsAllowed)
                return MediaUploadRejection.AttachmentsNotAllowed;

            const Permission attach = Permission.ViewSessions | Permission.SendMessages | Permission.AttachFiles;
            if (_permissions is { } permissions && (await permissions.PermissionsInAsync(userId, chat) & attach) != attach)
                return MediaUploadRejection.AttachmentsNotAllowed;
        }

        // A file is only downloaded, so its type is the sender's word; an image is shown and must be one
        if (upload.Kind == MediaUploadKind.File
                ? !IsMimeType(upload.MimeType)
                : !Limits.ChatImageMimeTypes.Contains(upload.MimeType))
            return MediaUploadRejection.TypeNotAllowed;

        // One snapshot for the whole check: the limits can be replaced meanwhile
        var limits = _limits;
        var maxSize = upload.Kind switch
        {
            MediaUploadKind.ImagePreview => Math.Min(Limits.ChatImagePreviewMaxSize, limits.MaxFileSizeBytes),
            MediaUploadKind.Avatar => Math.Min(Limits.AvatarMaxSize, limits.MaxFileSizeBytes),
            _ => limits.MaxFileSizeBytes
        };
        if (upload.Size > maxSize)
            return MediaUploadRejection.TooLarge;

        if (await _uploads.CountUnattachedAsync(_coordinatorInstanceId, userId, cancellationToken)
            >= _uploadSettings.MaxUnattachedUploadsPerUser)
            return MediaUploadRejection.TooManyUploads;

        if (limits.IsStorageLimited
            && await _uploads.GetTotalSizeAsync(_coordinatorInstanceId, cancellationToken) + upload.Size
            > limits.StorageQuotaBytes)
            return MediaUploadRejection.QuotaExceeded;

        return null;
    }

    public Task RegisterUploadAsync(string id, int userId, NewMediaUpload upload,
        CancellationToken cancellationToken = default) =>
        _uploads.AddAsync(new MediaUploadModel(id, _coordinatorInstanceId, userId, upload.ChatId, upload.Kind,
            upload.MimeType, upload.Size, MediaUploadState.Uploading, _timeProvider.GetUtcNow().UtcDateTime),
            cancellationToken);

    public async Task<bool> CompleteUploadAsync(string id, CancellationToken cancellationToken = default)
    {
        var upload = await GetOwnAsync(id, cancellationToken);
        if (upload == null)
            return false;

        if (await HasDeclaredTypeAsync(upload, cancellationToken)
            && await _uploads.MarkCompletedAsync(id, cancellationToken))
            return true;

        _logger.LogWarning("Upload {UploadId} of user {UserId} is not a {MimeType} file. Removing it.",
            id, upload.OwnerUserId, upload.MimeType);
        await RemoveAsync(id, cancellationToken);
        return false;
    }

    public async Task<bool> CanReadUploadAsync(string id, int userId, CancellationToken cancellationToken = default) =>
        await GetOwnAsync(id, cancellationToken) is { } upload && upload.IsOwnedBy(userId);

    public async Task<bool> CanWriteUploadAsync(string id, int userId, CancellationToken cancellationToken = default) =>
        await GetOwnAsync(id, cancellationToken) is { State: MediaUploadState.Uploading } upload
        && upload.IsOwnedBy(userId);

    public async Task<bool> CanDeleteUploadAsync(string id, int userId, CancellationToken cancellationToken = default) =>
        await GetOwnAsync(id, cancellationToken) is { State: not MediaUploadState.Attached } upload
        && upload.IsOwnedBy(userId);

    public Task ForgetUploadAsync(string id, CancellationToken cancellationToken = default) =>
        _uploads.RemoveAsync(id, cancellationToken);

    public async Task<MediaFile?> OpenImageAsync(int imageId, bool preview, CancellationToken cancellationToken = default)
    {
        var image = await _uploads.GetImageAsync(imageId, cancellationToken);
        if (image == null || image.IsMessageDeleted || !_chats.TryGetSession(image.ChatId, out _))
            return null;

        var upload = await GetOwnAsync(preview ? image.PreviewUploadId : image.UploadId, cancellationToken);
        if (upload is not { State: MediaUploadState.Attached })
            return null;

        var content = await _storage.OpenReadAsync(upload.Id, cancellationToken);
        return content == null ? null : new MediaFile(content, upload.MimeType);
    }

    public async Task<MediaFile?> OpenFileAsync(int fileId, CancellationToken cancellationToken = default)
    {
        var file = await _uploads.GetFileAsync(fileId, cancellationToken);
        if (file == null || file.IsMessageDeleted || !_chats.TryGetSession(file.ChatId, out _))
            return null;

        var upload = await GetOwnAsync(file.UploadId, cancellationToken);
        if (upload is not { Kind: MediaUploadKind.File, State: MediaUploadState.Attached })
            return null;

        var content = await _storage.OpenReadAsync(upload.Id, cancellationToken);
        return content == null ? null : new MediaFile(content, upload.MimeType, file.FileName);
    }

    public async Task<bool> AttachAvatarAsync(string uploadId, int userId, CancellationToken cancellationToken = default) =>
        await GetOwnAsync(uploadId, cancellationToken) is { Kind: MediaUploadKind.Avatar, State: MediaUploadState.Completed } upload
        && upload.IsOwnedBy(userId)
        && await _uploads.MarkAttachedAsync(uploadId, cancellationToken);

    public async Task RemoveAvatarAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        if (await GetOwnAsync(uploadId, cancellationToken) is { Kind: MediaUploadKind.Avatar })
            await RemoveAsync(uploadId, cancellationToken);
    }

    public async Task<MediaFile?> OpenAvatarAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        var upload = await GetOwnAsync(uploadId, cancellationToken);
        if (upload is not { Kind: MediaUploadKind.Avatar, State: MediaUploadState.Attached })
            return null;

        var content = await _storage.OpenReadAsync(upload.Id, cancellationToken);
        return content == null ? null : new MediaFile(content, upload.MimeType);
    }

    public async Task CleanUpAsync(CancellationToken cancellationToken = default)
    {
        var createdBefore = _timeProvider.GetUtcNow().UtcDateTime - UnattachedLifetime;
        var removed = await _uploads.RemoveUnattachedAsync(_coordinatorInstanceId, createdBefore, cancellationToken);
        foreach (var id in removed)
            await DeleteFileAsync(id, cancellationToken);

        if (removed.Count > 0)
            _logger.LogInformation("Removed {Count} uploads that were never attached to a message.", removed.Count);
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }

    private async Task<MediaUploadModel?> GetOwnAsync(string id, CancellationToken cancellationToken)
    {
        var upload = await _uploads.GetAsync(id, cancellationToken);
        return upload?.CoordinatorInstanceId == _coordinatorInstanceId ? upload : null;
    }

    private async Task<bool> HasDeclaredTypeAsync(MediaUploadModel upload, CancellationToken cancellationToken)
    {
        // Nothing is decoded from a file, so there is nothing to check its bytes against
        if (upload.Kind == MediaUploadKind.File)
            return true;

        await using var content = await _storage.OpenReadAsync(upload.Id, cancellationToken);
        if (content == null)
            return false;

        var header = new byte[MediaSignatures.HeaderLength];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        return MediaSignatures.Matches(upload.MimeType, header.AsSpan(0, read));
    }

    /// <summary>A <c>type/subtype</c> without parameters, as an HTTP header can carry it.</summary>
    private static bool IsMimeType(string value)
    {
        var slash = value.IndexOf('/');
        return slash > 0 && slash < value.Length - 1 && value.IndexOf('/', slash + 1) < 0
               && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '.' or '+' or '-' or '_');
    }

    private async Task RemoveAsync(string id, CancellationToken cancellationToken)
    {
        await _uploads.RemoveAsync(id, cancellationToken);
        await DeleteFileAsync(id, cancellationToken);
    }

    private async Task DeleteFileAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            await _storage.DeleteAsync(id, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Deleting the file of upload {UploadId} failed.", id);
        }
    }

    private async Task CleanUpLoopAsync()
    {
        var token = _stopping.Token;
        try
        {
            using var timer = new PeriodicTimer(CleanUpInterval, _timeProvider);
            do
            {
                try
                {
                    await CleanUpAsync(token);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(exception, "Cleaning up uploads failed.");
                }
            } while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException)
        {
        }
    }
}
