using Server.Application.Orchestrator;

namespace Server.Asp.Web.Realtime;

/// <summary>
/// Tells open panels when a coordinator is attached, starts, fails to start or is detached, so the overview and
/// the coordinator page show its state without polling.
/// </summary>
public sealed class OrchestratorPanelEvents(IOrchestratorInstance orchestrator, IPanelNotifier notifier)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        orchestrator.CoordinatorChanged += notifier.NotifyCoordinatorChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        orchestrator.CoordinatorChanged -= notifier.NotifyCoordinatorChanged;
        return Task.CompletedTask;
    }
}
