namespace Server.Asp.Web.Realtime.Sse;

public static class PanelSseExtensions
{
    public static IServiceCollection AddPanelSse(this IServiceCollection services)
    {
        services.AddSingleton<SsePanelNotifier>();
        services.AddSingleton<IPanelNotifier>(provider => provider.GetRequiredService<SsePanelNotifier>());
        services.AddHostedService(provider => provider.GetRequiredService<SsePanelNotifier>());
        return services;
    }

    /// <summary>The panel's change stream; see <see cref="PanelEventTypes"/> for what it carries.</summary>
    public static IEndpointRouteBuilder MapPanelSse(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(PanelRoutes.Events, (SsePanelNotifier notifier, CancellationToken ct) =>
            TypedResults.ServerSentEvents(notifier.SubscribeAsync(ct)));
        return endpoints;
    }
}
