using Server.Core.Models.Orchestrator;

namespace Server.Application.Orchestrator.Settings;

/// <summary>The orchestrator's global SFU node pool kept in the database.</summary>
public interface ISfuNodeStore
{
    /// <summary>Raised after a node was added, changed or removed.</summary>
    event Func<Task>? Changed;

    Task<IReadOnlyCollection<SfuNodeSettingsModel>> GetAllAsync(CancellationToken ct = default);
    Task<int> AddAsync(SfuNodeSettingsModel node, CancellationToken ct = default);
    Task UpdateAsync(SfuNodeSettingsModel node, CancellationToken ct = default);
    Task RemoveAsync(int nodeId, CancellationToken ct = default);
}
