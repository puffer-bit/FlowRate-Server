using Server.Core.Models.Coordinator;

namespace Server.Application.Orchestrator.Settings;

/// <summary>
/// Coordinator settings kept in the database. Editors go through <see cref="ICoordinatorSettingsService"/>,
/// which checks a change before it gets here.
/// </summary>
public interface ICoordinatorSettingsStore
{
    /// <summary>Raised with the coordinator id after its settings were added or changed.</summary>
    event Func<string, Task>? Changed;

    Task<IReadOnlyCollection<CoordinatorInstanceModel>> GetAllAsync(CancellationToken ct = default);
    Task<CoordinatorInstanceModel?> GetAsync(string coordinatorId, CancellationToken ct = default);
    Task AddAsync(CoordinatorInstanceModel coordinator, CancellationToken ct = default);
    /// <summary>Saves the settings under the next <see cref="CoordinatorInstanceModel.Revision"/>.</summary>
    Task UpdateAsync(CoordinatorInstanceModel coordinator, CancellationToken ct = default);
}
