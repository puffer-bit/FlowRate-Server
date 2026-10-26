using Server.Application.Orchestrator.SFUPool;

namespace Server.Asp.Web.Realtime;

/// <summary>Tells open panels when an SFU node connects, loses its connection or stops answering.</summary>
public sealed class SfuNodePanelEvents(ISfuNodeRegistry registry, IPanelNotifier notifier) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        registry.NodeStatusChanged += notifier.NotifySfuNodesChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        registry.NodeStatusChanged -= notifier.NotifySfuNodesChanged;
        return Task.CompletedTask;
    }
}
