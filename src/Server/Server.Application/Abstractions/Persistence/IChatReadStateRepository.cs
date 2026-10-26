using Server.Core.Models.TextChat;

namespace Server.Application.Abstractions.Persistence;

public interface IChatReadStateRepository
{
    Task<IReadOnlyDictionary<int, int>> GetPositionsAsync(int userId,
        CancellationToken cancellationToken = default);

    Task<int> MoveAsync(int userId, int chatId, int messageId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatUnreadMessage>> GetUnreadAsync(int chatId, int userId, int afterMessageId, int take,
        CancellationToken cancellationToken = default);

    Task<int> GetLastMessageIdAsync(int chatId,
        CancellationToken cancellationToken = default);
}
