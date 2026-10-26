using Server.Core.Models.Media;

namespace Server.Application.Abstractions.Persistence;

public interface IMediaUploadRepository
{
    Task AddAsync(MediaUploadModel upload, CancellationToken cancellationToken = default);

    Task<MediaUploadModel?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> MarkCompletedAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> MarkAttachedAsync(string id, CancellationToken cancellationToken = default);

    Task RemoveAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> RemoveByMessageAsync(int messageId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> RemoveByChatAsync(int chatId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> RemoveUnattachedAsync(string coordinatorInstanceId, DateTime createdBefore,
        CancellationToken cancellationToken = default);

    Task<long> GetTotalSizeAsync(string coordinatorInstanceId, CancellationToken cancellationToken = default);

    Task<int> CountUnattachedAsync(string coordinatorInstanceId, int ownerUserId,
        CancellationToken cancellationToken = default);

    Task<ChatImageFileModel?> GetImageAsync(int imageId, CancellationToken cancellationToken = default);

    Task<ChatFileDownloadModel?> GetFileAsync(int fileId, CancellationToken cancellationToken = default);
}

public sealed class MediaUploadUnavailableException() : Exception("An upload of the message cannot be attached.");
