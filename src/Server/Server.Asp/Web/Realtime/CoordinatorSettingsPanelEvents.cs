using Server.Application.Orchestrator.Settings;

namespace Server.Asp.Web.Realtime;

/// <summary>
/// Tells open panels about every saved settings change, whoever made it. The name or the endpoint may have changed
/// as well, so the coordinator itself is announced changed too.
/// </summary>
public sealed class CoordinatorSettingsPanelEvents(ICoordinatorSettingsService settingsService, IPanelNotifier notifier)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        settingsService.SettingsChanged += Notify;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        settingsService.SettingsChanged -= Notify;
        return Task.CompletedTask;
    }

    private void Notify(string coordinatorId)
    {
        notifier.NotifyCoordinatorSettingsChanged(coordinatorId);
        notifier.NotifyCoordinatorChanged(coordinatorId);
    }
}
