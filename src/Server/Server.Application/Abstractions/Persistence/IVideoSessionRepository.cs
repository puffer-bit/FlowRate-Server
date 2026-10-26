using Server.Core.Models.HybridSession;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Sessions.Hybrid;

namespace Server.Application.Abstractions.Persistence;

public interface IVideoSessionRepository
{
    Task<HybridSessionModel?> GetByIdAsync(int videoSessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<HybridSessionModel>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<int> AddAsync(HybridSessionModel instance, CancellationToken cancellationToken = default);
    Task UpdateAsync(HybridSessionModel instance, CancellationToken cancellationToken = default);
    Task RemoveAsync(int videoSessionId, CancellationToken cancellationToken = default);
    Task<List<HybridSessionModel>> GetByCoordinatorIdAsync(string coordinatorInstanceId,
        CancellationToken cancellationToken = default);
}