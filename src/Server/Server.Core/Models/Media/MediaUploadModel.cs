namespace Server.Core.Models.Media;

public enum MediaUploadKind
{
    Image = 0,
    ImagePreview = 1,
    Avatar = 2,
    File = 3
}

public enum MediaUploadState
{
    Uploading = 0,
    Completed = 1,
    Attached = 2
}

public sealed record MediaUploadModel(
    string Id,
    string CoordinatorInstanceId,
    int OwnerUserId,
    int? ChatId,
    MediaUploadKind Kind,
    string MimeType,
    long Size,
    MediaUploadState State,
    DateTime CreatedAt,
    int? MessageId = null)
{
    public bool IsOwnedBy(int userId) => OwnerUserId == userId;
}

public sealed record ChatImageFileModel(
    int ImageId,
    int ChatId,
    bool IsMessageDeleted,
    string MimeType,
    string UploadId,
    string PreviewUploadId);

public sealed record ChatFileDownloadModel(
    int FileId,
    int ChatId,
    bool IsMessageDeleted,
    string FileName,
    string UploadId);
