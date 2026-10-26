using Server.Core.Models.Coordinator;

namespace Server.Application.Coordinator;

/// <summary>A running coordinator: its stored configuration plus values that exist only at runtime.</summary>
public interface ICoordinatorInstanceContext
{
    /// <summary>The current configuration. Replaced as a whole when the settings change, so read it once per use.</summary>
    CoordinatorInstanceModel Model { get; }
    string ServerVersion { get; }

    string Id => Model.Id;
    string Name => Model.Name;
    TimeSpan UpTime => DateTime.UtcNow - Model.CurrentLaunchTime;

    /// <summary>Takes new settings of the same coordinator, keeping the launch time of the running one.</summary>
    void ApplySettings(CoordinatorInstanceModel model);
}
