using Server.Core.Models.Coordinator;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Sessions.Hybrid;

namespace Server.Application.Abstractions.Persistence;

public interface ICoordinatorInstanceRepository
{
    Task<CoordinatorInstanceModel?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<CoordinatorInstanceModel>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(CoordinatorInstanceModel instance, CancellationToken cancellationToken = default);
    Task UpdateAsync(CoordinatorInstanceModel instance, CancellationToken cancellationToken = default);
    Task RemoveAsync(string id, CancellationToken cancellationToken = default);
}