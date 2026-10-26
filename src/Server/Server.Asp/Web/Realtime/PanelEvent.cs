namespace Server.Asp.Web.Realtime;

/// <summary>A signal sent to the panel; <see cref="Type"/> is one of the <see cref="PanelEventTypes"/>.</summary>
public sealed record PanelEvent(string Type, string? CoordinatorId = null);

public static class PanelEventTypes
{
    /// <summary>Sent once when the stream opens; the panel should load everything it shows.</summary>
    public const string Connected = "connected";

    /// <summary>Sessions, peers or users of one coordinator changed.</summary>
    public const string CoordinatorChanged = "coordinatorChanged";

    /// <summary>The stored settings of one coordinator changed, from the panel or from a client.</summary>
    public const string CoordinatorSettingsChanged = "coordinatorSettingsChanged";

    /// <summary>Roles of one coordinator, or who has them, changed, from the panel or from a client.</summary>
    public const string CoordinatorRolesChanged = "coordinatorRolesChanged";

    /// <summary>The connection state of an SFU node changed. Round trips are not signalled; the panel polls them.</summary>
    public const string SfuNodesChanged = "sfuNodesChanged";

    /// <summary>Keeps idle connections open through proxies.</summary>
    public const string Ping = "ping";
}
