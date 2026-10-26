using Server.Core.Models.Orchestrator;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Sessions.Hybrid;

namespace Server.Application.Abstractions.Persistence;

public interface ISfuNodeRepository
{
    Task<IReadOnlyCollection<SfuNodeSettingsModel>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<int> AddAsync(SfuNodeSettingsModel node, CancellationToken cancellationToken = default);
    Task UpdateAsync(SfuNodeSettingsModel node, CancellationToken cancellationToken = default);
    Task RemoveAsync(int id, CancellationToken cancellationToken = default);
}
