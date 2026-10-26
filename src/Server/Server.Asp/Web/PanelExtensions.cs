using Server.Asp.Web.Realtime;
using Server.Asp.Web.Realtime.Sse;

namespace Server.Asp.Web;

public static class PanelExtensions
{
    /// <summary>The panel's change stream and everything that feeds it.</summary>
    public static IServiceCollection AddPanel(this IServiceCollection services)
    {
        services.AddPanelSse();
        services.AddHostedService<OrchestratorPanelEvents>();
        services.AddHostedService<CoordinatorSettingsPanelEvents>();
        services.AddHostedService<RolePanelEvents>();
        services.AddHostedService<SfuNodePanelEvents>();
        return services;
    }

    /// <summary>The panel's API, its change stream and the single-page app itself.</summary>
    public static WebApplication MapPanel(this WebApplication app)
    {
        app.UseStaticFiles();
        app.MapControllers();
        app.MapPanelSse();

        // The panel's own router handles the paths under it
        app.MapGet("/", () => Results.Redirect(PanelRoutes.App)).ExcludeFromDescription();
        app.MapFallbackToFile("panel/{**path:nonfile}", "panel/index.html");
        return app;
    }
}
