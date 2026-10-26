namespace Server.Asp.Web.Realtime;

/// <summary>
/// Pushes change signals to open control panels. The panel refetches over REST when told something changed,
/// so the transport behind this interface only has to deliver signals and can be swapped (SSE now).
/// </summary>
public interface IPanelNotifier
{
    void NotifyCoordinatorChanged(string coordinatorId);

    /// <summary>The stored settings of the coordinator changed, from the panel or from a client.</summary>
    void NotifyCoordinatorSettingsChanged(string coordinatorId);

    /// <summary>Roles of the coordinator, or who has them, changed, from the panel or from a client.</summary>
    void NotifyCoordinatorRolesChanged(string coordinatorId);

    /// <summary>An SFU node connected, lost its connection, stopped answering, or the pool changed.</summary>
    void NotifySfuNodesChanged();
}
