using Server.Core.Models.TextChat;

namespace Server.Application.Abstractions.Persistence;

public interface IChatReadStateRepository
{
    Task<IReadOnlyDictionary<int, int>> GetPositionsAsync(int userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the position of the user forward to <paramref name="messageId"/>. With <paramref name="markAt"/> a
    /// real move is also kept as a read mark with that time, so readers of a message get their read time.
    /// </summary>
    Task<ChatReadMove> MoveAsync(int userId, int chatId, int messageId, DateTime? markAt,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatUnreadMessage>> GetUnreadAsync(int chatId, int userId, int afterMessageId, int take,
        CancellationToken cancellationToken = default);

    Task<int> GetLastMessageIdAsync(int chatId,
        CancellationToken cancellationToken = default);

    /// <summary>The furthest position of the users other than <paramref name="userId"/> in each of the chats.</summary>
    Task<IReadOnlyDictionary<int, int>> GetPeerPositionsAsync(int userId, IReadOnlyCollection<int> chatIds,
        CancellationToken cancellationToken = default);

    /// <summary>Users other than <paramref name="exceptUserId"/> whose position reached the message, with the
    /// time of the first mark that did.</summary>
    Task<IReadOnlyList<ChatMessageReader>> GetReadersAsync(int chatId, int messageId, int exceptUserId,
        CancellationToken cancellationToken = default);

    Task<ChatReadTarget?> GetTargetAsync(int chatId, int messageId,
        CancellationToken cancellationToken = default);
}
