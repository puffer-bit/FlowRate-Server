namespace Server.Asp.Web;

/// <summary>Where the control panel and its API live; the frontend calls the same paths.</summary>
public static class PanelRoutes
{
    /// <summary>REST endpoints of the panel.</summary>
    public const string Api = "api/panel";

    /// <summary>Endpoints about one coordinator.</summary>
    public const string Coordinator = Api + "/coordinators/{id}";

    /// <summary>The change stream, see <see cref="Realtime.PanelEventTypes"/>.</summary>
    public const string Events = "/" + Api + "/events";

    /// <summary>The single-page app built into Web/wwwroot/panel.</summary>
    public const string App = "/panel/";
}
