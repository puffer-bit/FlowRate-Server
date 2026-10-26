using Server.Core.Models.Orchestrator;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Sessions.Hybrid;

namespace Server.Application.Abstractions.Persistence;

public interface IOrchestratorInstanceRepository
{
    Task<OrchestratorInstanceModel?> GetAsync(CancellationToken cancellationToken = default);
    Task SaveOrUpdateAsync(OrchestratorInstanceModel instance, CancellationToken cancellationToken = default);
}