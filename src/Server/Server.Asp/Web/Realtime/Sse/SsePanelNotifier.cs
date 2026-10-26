using System.Collections.Concurrent;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Server.Asp.Web.Realtime.Sse;

/// <summary>
/// Delivers panel signals over server-sent events. Signals are coalesced: a burst of changes in one
/// coordinator (peers joining, muting...) reaches each panel as a single event per flush.
/// </summary>
public sealed class SsePanelNotifier : BackgroundService, IPanelNotifier
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<Guid, ChannelWriter<PanelEvent>> _subscribers = new();
    private readonly ConcurrentDictionary<string, byte> _changedCoordinators = new();
    private readonly ConcurrentDictionary<string, byte> _changedSettings = new();
    private readonly ConcurrentDictionary<string, byte> _changedRoles = new();
    private readonly CancellationToken _applicationStopping;
    private int _sfuNodesChanged;

    public SsePanelNotifier(IHostApplicationLifetime lifetime)
    {
        // Open streams would otherwise hold the server's shutdown until its timeout
        _applicationStopping = lifetime.ApplicationStopping;
        _applicationStopping.Register(CompleteAll);
    }

    public void NotifyCoordinatorChanged(string coordinatorId) => _changedCoordinators.TryAdd(coordinatorId, 0);

    public void NotifyCoordinatorSettingsChanged(string coordinatorId) => _changedSettings.TryAdd(coordinatorId, 0);

    public void NotifyCoordinatorRolesChanged(string coordinatorId) => _changedRoles.TryAdd(coordinatorId, 0);

    public void NotifySfuNodesChanged() => Interlocked.Exchange(ref _sfuNodesChanged, 1);

    public async IAsyncEnumerable<SseItem<PanelEvent>> SubscribeAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<PanelEvent>(new UnboundedChannelOptions { SingleReader = true });
        var id = Guid.NewGuid();
        _subscribers[id] = channel.Writer;

        try
        {
            if (_applicationStopping.IsCancellationRequested)
                yield break;

            // Also makes the response start right away, so the browser sees the stream as open
            yield return ToItem(new PanelEvent(PanelEventTypes.Connected));

            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
                yield return ToItem(evt);
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(FlushInterval);
        var sinceLastPing = TimeSpan.Zero;

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                Flush(_changedCoordinators, PanelEventTypes.CoordinatorChanged);
                Flush(_changedSettings, PanelEventTypes.CoordinatorSettingsChanged);
                Flush(_changedRoles, PanelEventTypes.CoordinatorRolesChanged);
                if (Interlocked.Exchange(ref _sfuNodesChanged, 0) == 1)
                    Broadcast(new PanelEvent(PanelEventTypes.SfuNodesChanged));

                sinceLastPing += FlushInterval;
                if (sinceLastPing >= PingInterval)
                {
                    sinceLastPing = TimeSpan.Zero;
                    Broadcast(new PanelEvent(PanelEventTypes.Ping));
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        CompleteAll();
        return base.StopAsync(cancellationToken);
    }

    private void Flush(ConcurrentDictionary<string, byte> changed, string type)
    {
        foreach (var coordinatorId in changed.Keys)
        {
            if (changed.TryRemove(coordinatorId, out _))
                Broadcast(new PanelEvent(type, coordinatorId));
        }
    }

    private void Broadcast(PanelEvent evt)
    {
        foreach (var writer in _subscribers.Values)
            writer.TryWrite(evt);
    }

    private void CompleteAll()
    {
        foreach (var writer in _subscribers.Values)
            writer.TryComplete();
    }

    private static SseItem<PanelEvent> ToItem(PanelEvent evt) => new(evt, evt.Type);
}
