using Server.Core.Models.TextChat.Messages;

namespace Server.Application.Abstractions.Persistence;

public interface IMessagesRepository
{
    /// <returns>The message as it was stored: with its id, its revision and the ids of its attachments.</returns>
    Task<ChatMessageModel> AddAsync(ChatMessageModel message, CancellationToken cancellationToken = default);

    Task<List<ChatMessageModel>> GetByTakeSkipAsync(int chatId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    Task<List<ChatMessageModel>> GetBeforeAsync(int chatId,
        int? beforeMessageId,
        int take,
        CancellationToken cancellationToken = default);

    Task<List<ChatMessageModel>> GetChangesAsync(int chatId,
        long afterRevision,
        int take,
        CancellationToken cancellationToken = default);

    Task<int> CountChangesAsync(int chatId,
        long afterRevision,
        CancellationToken cancellationToken = default);

    Task<long> GetRevisionAsync(int chatId,
        CancellationToken cancellationToken = default);

    Task<ChatMessageModel?> GetByIdAsync(int messageId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatMessageModel>> GetLastMessagesAsync(int chatId,
        int count,
        CancellationToken cancellationToken = default);

    Task<long?> DeleteAsync(int messageId,
        CancellationToken cancellationToken = default);
    
    Task PurgeAsync(
        string chatId,
        CancellationToken cancellationToken = default);

    Task<long> UpdateAsync(
        ChatMessageModel message,
        CancellationToken cancellationToken = default);
}
