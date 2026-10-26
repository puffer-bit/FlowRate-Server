using Server.Core.Models.Coordinator.Settings;
using Server.Core.Models.Media;

namespace Server.Application.Coordinator.Media;

public enum MediaUploadRejection
{
    InvalidMetadata,
    ChatNotExists,
    AttachmentsNotAllowed,
    TypeNotAllowed,
    TooLarge,
    TooManyUploads,
    QuotaExceeded
}

public sealed record NewMediaUpload(int? ChatId, MediaUploadKind Kind, string MimeType, long Size);

/// <param name="FileName">Set for a file that is downloaded rather than shown.</param>
public sealed record MediaFile(Stream Content, string MimeType, string? FileName = null);

public interface IMediaService : IDisposable
{
    long MaxFileSizeBytes { get; }
    TimeSpan UnattachedLifetime { get; }

    /// <summary>Applies to uploads started from now on.</summary>
    void SetLimits(CoordinatorMediaSettings limits);

    /// <summary>Takes new limits of unattached uploads and of media tokens; issued tokens stay as they are.</summary>
    void SetUploadSettings(CoordinatorUploadSettings uploadSettings);

    void Start();

    (string Token, DateTime ExpiresAt) IssueToken(int userId);
    bool TryAuthorize(string? token, out int userId);

    /// <summary>
    /// Whether the holder of a valid token may still use the media: a token outlives a ban or a deletion,
    /// and a frozen user may only read.
    /// </summary>
    Task<bool> IsAllowedAsync(int userId, bool isUpload, CancellationToken cancellationToken = default);

    Task<MediaUploadRejection?> CheckNewUploadAsync(int userId, NewMediaUpload upload,
        CancellationToken cancellationToken = default);
    Task RegisterUploadAsync(string id, int userId, NewMediaUpload upload, CancellationToken cancellationToken = default);
    Task<bool> CompleteUploadAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> CanReadUploadAsync(string id, int userId, CancellationToken cancellationToken = default);
    Task<bool> CanWriteUploadAsync(string id, int userId, CancellationToken cancellationToken = default);
    Task<bool> CanDeleteUploadAsync(string id, int userId, CancellationToken cancellationToken = default);
    Task ForgetUploadAsync(string id, CancellationToken cancellationToken = default);

    Task<MediaFile?> OpenImageAsync(int imageId, bool preview, CancellationToken cancellationToken = default);

    Task<MediaFile?> OpenFileAsync(int fileId, CancellationToken cancellationToken = default);

    Task<bool> AttachAvatarAsync(string uploadId, int userId, CancellationToken cancellationToken = default);
    Task RemoveAvatarAsync(string uploadId, CancellationToken cancellationToken = default);
    Task<MediaFile?> OpenAvatarAsync(string uploadId, CancellationToken cancellationToken = default);

    Task CleanUpAsync(CancellationToken cancellationToken = default);
}
