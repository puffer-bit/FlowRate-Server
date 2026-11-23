using Server.Core.Enums;
using Server.Core.Models.Coordinator;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Snapshots;

namespace Server.Application.Coordinator;

public interface ICoordinatorInstance : IDisposable
{
    Moderation.ModerationService? Moderation => null;
    /// <summary>The devices of the users' accounts, to remove one and disconnect it from the control panel.</summary>
    Authentication.UserSessions? Devices => null;
    ICoordinatorInstanceContext Context { get; }
    CoordinatorState State { get; }
    TimeSpan UpTime => Context.UpTime;

    int ConnectedUsersCount { get; }
    int ActiveSessionsCount { get; }

    /// <summary>Loads the sessions and opens the gateway for clients.</summary>
    Task StartAsync();
    /// <param name="notice">What the connected users are told; a restart when not given.</param>
    Task CloseAsync(Shared.Models.Notifications.Heartbeat.ServerRestartNotification? notice = null);

    /// <summary>Sends a message to every connected user.</summary>
    void Broadcast(Shared.Models.BaseMessage message) { }

    /// <summary>
    /// Applies changed settings without a restart and tells the connected users. The listening endpoint is not
    /// changed: the orchestrator restarts the coordinator for that.
    /// </summary>
    void ApplySettings(CoordinatorInstanceModel model);

    CoordinatorSnapshot GetSnapshot();
}
