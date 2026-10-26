using Microsoft.Extensions.Logging;
using Server.Application.Coordinator.Connections;

namespace Server.Application.Coordinator.Heartbeat;

public class HeartbeatManager : IHeartbeatManager
{
    public event Action<string>? TimeoutOccured;

    private readonly ILogger _logger;

    private readonly IConnectionsContext _connectionsContext;
    private volatile HeartbeatSettings _settings;

    private int _monitorRunning;

    public HeartbeatManager(
        ILogger logger,
        IConnectionsContext connectionsContext,
        HeartbeatSettings settings)
    {
        _logger = logger;

        _connectionsContext = connectionsContext;
        _settings = settings;
    }

    public Task StartAsync(string connectionId)
    {
        if (Interlocked.CompareExchange(ref _monitorRunning, 1, 0) == 0)
            _ = Task.Run(MonitorAsync);

        return Task.CompletedTask;
    }

    public void RegisterHeartbeat(string connectionId)
    {
        if (_connectionsContext.ClientConnections.TryGetValue(connectionId, out var connection))
        {
            connection.LastHeartbeat = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    public void ApplySettings(HeartbeatSettings settings) => _settings = settings;

    private async Task MonitorAsync()
    {
        using var timer = new PeriodicTimer(_settings.Interval);

        while (await timer.WaitForNextTickAsync())
        {
            var settings = _settings;
            if (timer.Period != settings.Interval)
                timer.Period = settings.Interval;

            var timeoutLimitMs = (long)settings.Timeout.TotalMilliseconds;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            foreach (var connection in _connectionsContext.ClientConnections.Values)
            {
                if (now - connection.LastHeartbeat <= timeoutLimitMs || connection.TokenSource.IsCancellationRequested)
                    continue;

                try
                {
                    TimeoutOccured?.Invoke(connection.ConnectionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Heartbeat timeout handling failed for connection {ConnectionId}",
                        connection.ConnectionId);
                }
            }

            if (!_connectionsContext.ClientConnections.IsEmpty)
                continue;

            Volatile.Write(ref _monitorRunning, 0);
            if (_connectionsContext.ClientConnections.IsEmpty
                || Interlocked.CompareExchange(ref _monitorRunning, 1, 0) != 0)
                return;
        }
    }
}
