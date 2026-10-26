using Shared.Enums;
using System.Diagnostics;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Server.Api.Grpc;
using Server.Application.Abstractions.Sfu;
using Server.Infrastructure.Mediasoup;

namespace Server.Infrastructure.Coordinator.Services.Mediasoup;

public partial class MediasoupGateway
{
    /// <summary>Pings in a row a node may miss while its channel is up before it is treated as lost.</summary>
    private const int MissedPingsLimit = 3;

    public event Func<Task>? ConnectionLost;
    public event Func<Task>? Connected;
    public event Action? StatusChanged;

    public bool IsConnected
    {
        get;
        private set;
    }

    public SfuNodeStatus Status => _status;

    private volatile SfuNodeStatus _status = SfuNodeStatus.Initial();
    private readonly Lock _statusLock = new();
    private int _missedPings;

    public async Task Connect()
    {
        await CloseChannelAsync();
        _cts = new CancellationTokenSource();

        var handler = new SocketsHttpHandler
        {
            KeepAlivePingDelay = TimeSpan.FromSeconds(_connectionOptions.KeepAlivePingSeconds),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(_connectionOptions.KeepAliveTimeoutSeconds),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always
        };

        var channel = GrpcChannel.ForAddress(_endpoint.Address, new GrpcChannelOptions
        {
            HttpHandler = handler
        });
        _activeChannel = channel;
        _grpcClient = new MediasoupServer.MediasoupServerClient(channel);
        UpdateStatus(SfuNodeConnectionState.Connecting, error: null);

        try
        {
            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(_connectionOptions.ConnectTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(connectCts.Token, _cts.Token);

            _logger.LogInformation($"Attempting to connect to gRPC server: {_endpoint.Address}...");

            await channel.ConnectAsync(linkedCts.Token);

            MarkReachable();
            _logger.LogInformation($"Successfully connected to gRPC server: {_endpoint.Address}");
        }
        catch (OperationCanceledException)
        {
            MarkUnreachable(SfuNodeConnectionState.Disconnected, "Connection timed out.");
            _logger.LogError($"Connection to gRPC server {_endpoint.Address} timed out or was cancelled");
        }
        catch (Exception ex)
        {
            MarkUnreachable(SfuNodeConnectionState.Disconnected, ex.Message);
            _logger.LogError(ex, "Failed to establish initial connection to gRPC server: {EndpointAddress}. Exception: {ExMessage}", _endpoint.Address, ex.Message);
        }

        // The channel keeps reconnecting by itself; these follow it until the next Connect or Disconnect
        _ = MonitorChannelStateAsync(channel, _cts.Token);
        _ = PingLoopAsync(channel, _cts.Token);
    }

    public async Task Disconnect()
    {
        await CloseChannelAsync();

        lock (_statusLock)
        {
            IsConnected = false;
            SetStatus(SfuNodeConnectionState.Disconnected, roundTripTime: null, error: "Disconnected by the orchestrator.");
        }
    }

    private async Task CloseChannelAsync()
    {
        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
            _cts = null;
        }

        if (_activeChannel != null)
        {
            await _activeChannel.ShutdownAsync();
            _activeChannel.Dispose();
            _activeChannel = null;
            _grpcClient = null;
        }
    }

    private async Task MonitorChannelStateAsync(GrpcChannel channel, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var currentState = channel.State;

                if (currentState == ConnectivityState.TransientFailure)
                    MarkUnreachable(SfuNodeConnectionState.Disconnected, "Connection with the node lost.");

                if (currentState == ConnectivityState.Ready)
                    MarkReachable();

                await channel.WaitForStateChangedAsync(currentState, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PingLoopAsync(GrpcChannel channel, CancellationToken ct)
    {
        var client = new MediasoupServer.MediasoupServerClient(channel);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_connectionOptions.PingIntervalSeconds));

        try
        {
            do
            {
                await PingAsync(client, channel, ct);
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PingAsync(MediasoupServer.MediasoupServerClient client, GrpcChannel channel,
        CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(_connectionOptions.CallTimeoutSeconds);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await client.PingAsync(new PingRequest(),
                new CallOptions(deadline: DateTime.UtcNow.Add(timeout), cancellationToken: ct));
            stopwatch.Stop();

            MarkReachable(stopwatch.Elapsed, new SfuNodeLoad(response.SessionCount, response.WorkerCount,
                TimeSpan.FromSeconds(response.UptimeSeconds)));
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Unimplemented)
        {
            // A node built before Ping still answered, so the round trip holds; it just reports no load
            MarkReachable(stopwatch.Elapsed);
        }
        catch (RpcException e) when (!ct.IsCancellationRequested)
        {
            var error = e.StatusCode == StatusCode.DeadlineExceeded
                ? $"The node did not answer a ping within {timeout.TotalSeconds:0} s."
                : e.Status.Detail;
            HandleMissedPing(channel, error);
        }
    }

    private void HandleMissedPing(GrpcChannel channel, string error)
    {
        // A channel that is down is reported by the monitor; here only a node that stopped answering is left
        if (channel.State != ConnectivityState.Ready)
        {
            MarkUnreachable(SfuNodeConnectionState.Disconnected, error);
            return;
        }

        bool isLost;
        lock (_statusLock)
        {
            _missedPings++;
            isLost = _missedPings >= MissedPingsLimit && IsConnected;
            if (isLost)
                IsConnected = false;
            SetStatus(SfuNodeConnectionState.Unresponsive, roundTripTime: null, error);
        }

        if (isLost)
        {
            // Calls to it would only time out: sessions drop their state and rejoin once the node answers again
            _logger.LogWarning("SFU node {NodeId} missed {Count} pings in a row. Treating the connection as lost.",
                NodeId, MissedPingsLimit);
            Raise(ConnectionLost, nameof(ConnectionLost));
        }
    }

    /// <summary>The channel is up or the node answered a ping.</summary>
    private void MarkReachable(TimeSpan? roundTripTime = null, SfuNodeLoad? load = null)
    {
        bool isRestored;
        lock (_statusLock)
        {
            // A ready channel says nothing about a node that stopped answering pings
            if (roundTripTime == null && _status.State == SfuNodeConnectionState.Unresponsive)
                return;

            _missedPings = 0;
            isRestored = !IsConnected;
            IsConnected = true;
            SetStatus(SfuNodeConnectionState.Connected, roundTripTime ?? _status.RoundTripTime, error: null,
                lastPingAtUtc: roundTripTime != null ? DateTimeOffset.UtcNow : null, load);
        }

        if (isRestored)
        {
            _logger.LogInformation("Connection with SFU node {NodeId} is up.", NodeId);
            Raise(Connected, nameof(Connected));
        }
    }

    private void MarkUnreachable(SfuNodeConnectionState state, string error)
    {
        bool isLost;
        lock (_statusLock)
        {
            isLost = IsConnected;
            IsConnected = false;
            SetStatus(state, roundTripTime: null, error);
        }

        if (isLost)
        {
            _logger.LogWarning("Connection with SFU node {NodeId} lost: {Error} Reconnecting automatically...",
                NodeId, error);
            Raise(ConnectionLost, nameof(ConnectionLost));
        }
    }

    private void UpdateStatus(SfuNodeConnectionState state, string? error)
    {
        lock (_statusLock)
            SetStatus(state, _status.RoundTripTime, error);
    }

    /// <remarks>
    /// Called under <see cref="_statusLock"/>, so <see cref="StatusChanged"/> handlers only take note and return.
    /// Null <paramref name="lastPingAtUtc"/> and <paramref name="load"/> keep the previous values.
    /// </remarks>
    private void SetStatus(SfuNodeConnectionState state, TimeSpan? roundTripTime, string? error,
        DateTimeOffset? lastPingAtUtc = null, SfuNodeLoad? load = null)
    {
        var previous = _status;
        var isStateChanged = previous.State != state;

        _status = previous with
        {
            State = state,
            RoundTripTime = roundTripTime,
            LastPingAtUtc = lastPingAtUtc ?? previous.LastPingAtUtc,
            StateChangedAtUtc = isStateChanged ? DateTimeOffset.UtcNow : previous.StateChangedAtUtc,
            Error = error,
            Load = load ?? previous.Load
        };

        if (isStateChanged)
            StatusChanged?.Invoke();
    }

    private void Raise(Func<Task>? handlers, string name)
    {
        if (handlers == null)
            return;

        // Every session subscribes; one failing must not keep the others from hearing about the node
        foreach (var handler in handlers.GetInvocationList().Cast<Func<Task>>())
            _ = RunHandlerAsync(handler, name);
    }

    private async Task RunHandlerAsync(Func<Task> handler, string name)
    {
        try
        {
            await handler();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handling {Event} of SFU node {NodeId} failed.", name, NodeId);
        }
    }
}
