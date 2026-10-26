using Server.Core.Models.TextChat;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Sessions.Hybrid;

namespace Server.Application.Abstractions.Persistence;

public interface IChatSessionRepository
{
    Task<ChatSessionModel?> GetByIdAsync(int chatId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ChatSessionModel>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<int> AddAsync(ChatSessionModel instance, CancellationToken cancellationToken = default);
    Task UpdateAsync(ChatSessionModel instance, CancellationToken cancellationToken = default);
    Task RemoveAsync(int chatId, CancellationToken cancellationToken = default);
    Task<List<ChatSessionModel>> GetByCoordinatorIdAsync(string coordinatorInstanceId,
        CancellationToken cancellationToken = default);
}