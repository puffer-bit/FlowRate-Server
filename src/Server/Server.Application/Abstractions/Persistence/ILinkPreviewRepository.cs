using Server.Core.Models.TextChat.Messages.Content;

namespace Server.Application.Abstractions.Persistence;

public interface ILinkPreviewRepository
{
    /// <summary>The newest preview of the address fetched at or after <paramref name="fetchedAfter"/>.</summary>
    Task<ChatLinkPreviewModel?> GetNewestAsync(string coordinatorInstanceId, string url, DateTime fetchedAfter,
        CancellationToken cancellationToken = default);

    Task<ChatLinkPreviewModel?> GetAsync(string coordinatorInstanceId, int id, CancellationToken cancellationToken = default);

    /// <returns>The preview with its id.</returns>
    Task<ChatLinkPreviewModel> AddAsync(ChatLinkPreviewModel preview, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the previews fetched before <paramref name="fetchedBefore"/> that no message shows.
    /// </summary>
    /// <returns>The ids of the removed previews, whose images are to be deleted.</returns>
    Task<IReadOnlyList<int>> RemoveUnusedAsync(string coordinatorInstanceId, DateTime fetchedBefore,
        CancellationToken cancellationToken = default);
}
