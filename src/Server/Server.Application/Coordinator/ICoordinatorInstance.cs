using Server.Core.Enums;
using Server.Core.Models.Coordinator;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Snapshots;

namespace Server.Application.Coordinator;

public interface ICoordinatorInstance : IDisposable
{
    ICoordinatorInstanceContext Context { get; }
    CoordinatorState State { get; }
    TimeSpan UpTime => Context.UpTime;

    int ConnectedUsersCount { get; }
    int ActiveSessionsCount { get; }

    /// <summary>Loads the sessions and opens the gateway for clients.</summary>
    Task StartAsync();
    Task CloseAsync();

    /// <summary>
    /// Applies changed settings without a restart and tells the connected users. The listening endpoint is not
    /// changed: the orchestrator restarts the coordinator for that.
    /// </summary>
    void ApplySettings(CoordinatorInstanceModel model);

    CoordinatorSnapshot GetSnapshot();
}
