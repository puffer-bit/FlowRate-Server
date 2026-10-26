using Server.Core.Models.Coordinator;
using Server.Core.Models.Orchestrator;

namespace Server.Application.Orchestrator.Settings;

/// <summary>
/// The one way to read and change coordinator settings, for the control panel and for the clients alike.
/// A saved change reaches the running coordinator at once: see <see cref="ICoordinatorSettingsStore.Changed"/>.
/// </summary>
public interface ICoordinatorSettingsService
{
    /// <summary>
    /// Raised with the coordinator id after its settings were saved, whoever changed them. Raised on the thread
    /// of the change, after the running coordinator took the settings.
    /// </summary>
    event Action<string>? SettingsChanged;

    Task<CoordinatorInstanceModel?> GetAsync(string coordinatorId, CancellationToken ct = default);

    /// <summary>The SFU nodes a coordinator can be given, by name.</summary>
    Task<IReadOnlyList<SfuNodeSettingsModel>> GetSfuNodesAsync(CancellationToken ct = default);

    /// <summary>
    /// Replaces the settings of the coordinator, unless they were changed after <paramref name="expectedRevision"/>
    /// or some values are refused.
    /// </summary>
    Task<CoordinatorSettingsUpdateResult> UpdateAsync(string coordinatorId, CoordinatorSettingsChange change,
        long expectedRevision, CancellationToken ct = default);
}

public enum CoordinatorSettingsUpdateStatus
{
    Updated,
    NotFound,

    /// <summary>The settings were changed after the expected revision; nothing was saved.</summary>
    Conflict,

    /// <summary>Some values were refused; nothing was saved.</summary>
    Invalid
}

/// <param name="Settings">The settings as they are now: the saved ones, or the unchanged ones. Null when not found.</param>
/// <param name="Errors">Why values were refused, by <see cref="CoordinatorSettingsFields">field</see>.</param>
public sealed record CoordinatorSettingsUpdateResult(
    CoordinatorSettingsUpdateStatus Status,
    CoordinatorInstanceModel? Settings,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static CoordinatorSettingsUpdateResult NotFound { get; } =
        new(CoordinatorSettingsUpdateStatus.NotFound, null, new Dictionary<string, string[]>());
}
