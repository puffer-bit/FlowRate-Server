using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Shared.Enums;
using Shared.Models.Media.Transport;
using Server.Core.Enums;
using Server.Core.Models.HybridSession;
using Server.Application.Coordinator.Events;
using Server.Application.Abstractions.Sfu;
using Server.Application.Coordinator.Authentication;

namespace Server.Application.Coordinator.Sessions.Hybrid.Peers;

/// <summary>
/// Membership of a hybrid session and its SFU gateway. Every state change runs under one lock,
/// so membership and media routing (see the Media part) never interleave.
/// </summary>
public partial class HybridSessionPeerManager : IHybridSessionPeerManager
{
    public bool IsReady => _gateway is { IsConnected: true };

    private HybridSessionModel Model { get; }

    /// <summary>Only valid after a successful <see cref="IsReady"/> check.</summary>
    private ISFUGateway Gateway => _gateway ?? throw new InvalidOperationException("SFU gateway is not assigned.");

    private readonly ILogger _logger;
    private readonly ICoordinatorEventBus _coordinatorEventBus;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Written under the lock only; concurrent so that snapshots can be read without it
    private readonly ConcurrentDictionary<int, Peer> _peers = new();

    private ISFUGateway? _gateway;
    private bool _isSessionLoaded;

    public HybridSessionPeerManager(
        ILogger logger,
        ICoordinatorEventBus eventBus,
        HybridSessionModel model)
    {
        Model = model;

        _logger = logger;
        _coordinatorEventBus = eventBus;
    }

    public ISFUGateway? CurrentGateway => _gateway;

    public Task ChangeGatewayAsync(ISFUGateway? gateway) => LockedAsync(async () =>
    {
        if (ReferenceEquals(_gateway, gateway))
        {
            await SetStateAsync(gateway == null ? HybridSessionNodeState.WaitingForSFUNode : HybridSessionNodeState.Active);
            return;
        }

        if (_gateway != null)
        {
            _gateway.ConnectionLost -= HandleGatewayConnectionLost;
            _gateway.Connected -= HandleGatewayConnected;

            // The old node may still be running: free what the session holds there
            if (_isSessionLoaded && _gateway.IsConnected)
                await UnloadSessionAsync();
        }

        _gateway = gateway;
        if (_gateway == null)
        {
            // Peers stay in the session and rejoin by themselves once a node is assigned again
            ResetSfuState();
            await SetStateAsync(HybridSessionNodeState.WaitingForSFUNode);
            return;
        }

        _gateway.ConnectionLost += HandleGatewayConnectionLost;
        _gateway.Connected += HandleGatewayConnected;

        await SetStateAsync(HybridSessionNodeState.Active);
        await DropSfuStateAsync(new HybridSessionSFUNodeChangedEvent(Model.Id));
    });

    public Task<(ErrorCode? result, TransportData? transportData)> AddPeerAsync(int userId,
        string clientEpoch, string? connectionId = null) => LockedAsync<(ErrorCode? result, TransportData? transportData)>(async () =>
    {
        if (!EnsureReady("join", userId))
            return (ErrorCode.SFUUnavailable, null);

        var isNew = !_peers.TryGetValue(userId, out var peer);
        if (isNew)
        {
            peer = Peer.Join(userId, Model, connectionId);
        }
        else
        {
            if (peer!.IsRejected)
            {
                _logger.LogTrace("User with ID {userId} failed to join. Participant was rejected", userId);
                return (ErrorCode.NotAdmitted, null);
            }

            var replaced = peer.ConnectionId is { } previous && previous != connectionId ? previous : null;
            await ReleasePeerMediaAsync(userId);
            peer.Rejoin(connectionId);

            // The device the user was on loses its media either way, so it is told even when the join fails
            if (replaced != null)
            {
                _logger.LogTrace("User with ID {userId} joined from another connection; the previous one is out.",
                    userId);
                await _coordinatorEventBus.PublishAsync(new HybridSessionPeerReplacedEvent(Model.Id, userId, replaced));
            }
        }

        if (!_isSessionLoaded)
        {
            await Gateway.LoadSession(Model.Id);
            _isSessionLoaded = true;
        }

        var (result, transportData) = await Gateway.CreatePeerTransport(Model.Id, userId, clientEpoch);
        if (result != SFUTransportCreateResult.NoError)
        {
            _logger.LogError("User with ID {userId} failed to join. SFU service return error: {error}", userId, result);
            if (_peers.IsEmpty)
                await UnloadSessionAsync();
            return (ErrorCode.SFUError, null);
        }

        // A new peer is added only once it has a transport, so a failed join leaves nothing behind
        if (isNew)
            _peers[userId] = peer;

        _logger.LogTrace("User with ID {userId} successful joined", userId);
        await _coordinatorEventBus.PublishAsync(new HybridSessionPeerJoinedEvent(Model.Id, peer.ToSnapshot()));
        return (null, transportData);
    }, timedOut: (ErrorCode.TimedOut, null));

    public Task<ErrorCode?> ConnectPeerAsync(int userId, DTLSParameters dtlsParameters) =>
        LockedAsync<ErrorCode?>(async () =>
    {
        if (!EnsureReady("connect", userId))
            return ErrorCode.SFUUnavailable;

        if (!_peers.TryGetValue(userId, out var peer))
        {
            _logger.LogError("User with ID {userId} failed to connect. Peer not exists", userId);
            return ErrorCode.Internal;
        }

        if (peer.IsRejected)
            return ErrorCode.NotAdmitted;

        var result = await Gateway.ConnectPeerTransport(Model.Id, userId, dtlsParameters);
        if (result != SFUTransportConnectResult.NoError)
        {
            _logger.LogError("User with ID {userId} failed to connect. SFU service return error: {error}", userId, result);
            return ErrorCode.SFUError;
        }

        peer.MarkTransportConnected();
        await _coordinatorEventBus.PublishAsync(new HybridSessionPeerStateChangedEvent(Model.Id, userId, peer.State));
        _logger.LogTrace("User with ID {userId} successful connect", userId);

        if (peer.CanReceiveMedia)
            await ConsumeExistingOutboundsAsync(userId);
        return null;
    }, timedOut: ErrorCode.SFUError);

    public Task<ErrorCode?> ApprovePeerAsync(int initiatorId, int userId) => LockedAsync<ErrorCode?>(async () =>
    {
        if (!EnsureReady("approve", userId))
            return ErrorCode.SFUUnavailable;

        if (!_peers.TryGetValue(userId, out var peer))
        {
            _logger.LogDebug("Attempt to approve a peer with ID {userId} failed. Peer not exists", userId);
            return ErrorCode.PeerNotFound;
        }

        var wasPending = peer.Admission == PeerAdmission.Pending;
        var result = peer.Approve();
        if (result != null)
        {
            _logger.LogDebug("Attempt to approve a peer with ID {userId} failed: {result}", userId, result);
            return result;
        }

        if (!wasPending)
            return null;

        await _coordinatorEventBus.PublishAsync(new HybridSessionPeerApprovedEvent(Model.Id, userId, initiatorId));
        await _coordinatorEventBus.PublishAsync(new HybridSessionPeerStateChangedEvent(Model.Id, userId, peer.State));

        if (peer.CanReceiveMedia)
            await ConsumeExistingOutboundsAsync(userId);

        _logger.LogTrace("Participant with ID {userId} approved.", userId);
        return null;
    }, timedOut: ErrorCode.TimedOut);

    public Task<ErrorCode?> RejectPeerAsync(int initiatorId, int userId, string? reason = null) =>
        LockedAsync<ErrorCode?>(async () =>
    {
        if (!EnsureReady("reject", userId))
            return ErrorCode.SFUUnavailable;

        if (!_peers.TryGetValue(userId, out var peer))
        {
            _logger.LogDebug("Attempt to reject a peer with ID {userId} failed. Peer not exists", userId);
            return ErrorCode.PeerNotFound;
        }

        var result = peer.Reject();
        if (result != null)
        {
            _logger.LogDebug("Attempt to reject a peer with ID {userId} failed: {result}", userId, result);
            return result;
        }

        // A rejected peer is taken out like a kicked one and may ask to join again
        await RemovePeerLockedAsync(userId, new HybridSessionPeerRejectedEvent(Model.Id, userId, initiatorId, reason));

        _logger.LogTrace("Participant with ID {userId} rejected.", userId);
        return null;
    }, timedOut: ErrorCode.TimedOut);

    public Task<ErrorCode?> KickPeerAsync(int initiatorId, int userId, string? reason = null) =>
        initiatorId == userId
            ? Task.FromResult<ErrorCode?>(ErrorCode.CannotTargetSelf)
            : LockedAsync(() => RemovePeerLockedAsync(userId,
                new HybridSessionPeerKickedEvent(Model.Id, userId, initiatorId, reason)), ErrorCode.TimedOut);

    public Task<ErrorCode?> BanPeerAsync(int initiatorId, int userId, string? reason = null)
    {
        throw new NotImplementedException();
    }

    public Task<ErrorCode?> RemovePeerAsync(int userId) =>
        LockedAsync(() => RemovePeerLockedAsync(userId, new HybridSessionPeerLeftEvent(Model.Id, userId)),
            ErrorCode.TimedOut);

    public Task<ErrorCode?> RemovePeerOfConnectionAsync(int userId, string connectionId) =>
        LockedAsync(() => _peers.TryGetValue(userId, out var peer) && peer.ConnectionId == connectionId
                ? RemovePeerLockedAsync(userId, new HybridSessionPeerLeftEvent(Model.Id, userId))
                : Task.FromResult<ErrorCode?>(ErrorCode.PeerNotFound),
            ErrorCode.TimedOut);

    public bool IsPeerOf(int userId, string connectionId) =>
        !_peers.TryGetValue(userId, out var peer) || peer.ConnectionId == null || peer.ConnectionId == connectionId;

    public Task KickAllPeersAsync(string? reason = null) => LockedAsync(() => KickAllPeersLockedAsync(reason));

    private async Task KickAllPeersLockedAsync(string? reason)
    {
        var canReachSfu = IsReady;

        foreach (var peer in _peers.Values)
        {
            if (canReachSfu)
                await ClosePeerTransportAsync(peer.UserId);

            peer.Close();
            await _coordinatorEventBus.PublishAsync(new HybridSessionPeerKickedEvent(Model.Id, peer.UserId, Reason: reason));
        }

        _peers.Clear();
        ResetMediaState();

        if (canReachSfu)
            await UnloadSessionAsync();
        else
            _isSessionLoaded = false;
    }

    public bool HasPeer(int userId) => _peers.ContainsKey(userId);

    public IReadOnlyDictionary<int, PeerSnapshot> GetAllPeerSnapshots() =>
        _peers.Values.ToDictionary(peer => peer.UserId, peer => peer.ToSnapshot());

    private async Task<ErrorCode?> RemovePeerLockedAsync<TEvent>(int userId, TEvent removedEvent)
        where TEvent : HybridSessionUpdatedEvent
    {
        if (!_peers.TryRemove(userId, out var peer))
        {
            _logger.LogDebug("Attempt to remove a peer with ID {userId} failed. Peer not exists.", userId);
            return ErrorCode.PeerNotFound;
        }

        // Without a reachable node the SFU state is already dropped, so leaving must not depend on it
        var canReachSfu = IsReady;
        if (canReachSfu)
        {
            await ReleasePeerMediaAsync(userId);
            await ClosePeerTransportAsync(userId);
        }

        peer.Close();
        // The concrete event type matters: the event bus dispatches on it
        await _coordinatorEventBus.PublishAsync(removedEvent);

        if (_peers.IsEmpty)
        {
            if (canReachSfu)
                await UnloadSessionAsync();
            else
                _isSessionLoaded = false;
        }

        _logger.LogTrace("User with ID {userId} left.", userId);
        return null;
    }

    private Task HandleGatewayConnectionLost() => LockedAsync(() =>
        DropSfuStateAsync(new HybridSessionSFUNodeDisconnectedEvent(Model.Id)));

    private async Task HandleGatewayConnected()
    {
        if (!_peers.IsEmpty)
            await _coordinatorEventBus.PublishAsync(new HybridSessionSFUNodeConnectedEvent(Model.Id));
    }

    /// <summary>
    /// Everything the SFU held for this session is gone: peers have to rejoin and the session has to be loaded again.
    /// </summary>
    private async Task DropSfuStateAsync<TEvent>(TEvent notification) where TEvent : HybridSessionUpdatedEvent
    {
        ResetSfuState();

        if (!_peers.IsEmpty)
            await _coordinatorEventBus.PublishAsync(notification);
    }

    private void ResetSfuState()
    {
        ResetMediaState();
        _isSessionLoaded = false;

        foreach (var peer in _peers.Values)
            peer.WaitForServer();
    }

    private async Task SetStateAsync(HybridSessionNodeState state)
    {
        if (!Model.SetState(state))
            return;

        if (state == HybridSessionNodeState.WaitingForSFUNode)
            _logger.LogWarning("Hybrid session {SessionId} waits for an SFU node.", Model.Id);

        await _coordinatorEventBus.PublishAsync(new HybridSessionStateChangedEvent(Model.Id, state));
    }

    private bool EnsureReady(string action, int userId)
    {
        if (IsReady)
            return true;

        _logger.LogError("User with ID {userId} failed to {action}. SFU service is not ready", userId, action);
        return false;
    }

    private async Task ClosePeerTransportAsync(int userId)
    {
        if (_gateway == null)
            return;

        try
        {
            await Gateway.ClosePeerTransport(Model.Id, userId);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to close the transport of user with ID {userId}", userId);
        }
    }

    private async Task UnloadSessionAsync()
    {
        _isSessionLoaded = false;
        if (_gateway == null)
            return;

        try
        {
            await Gateway.UnloadSession(Model.Id);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to unload the SFU session");
        }
    }

    private async Task LockedAsync(Func<Task> action)
    {
        await _lock.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <param name="timedOut">Returned when the SFU did not answer in time.</param>
    private async Task<T> LockedAsync<T>(Func<Task<T>> action, T timedOut)
    {
        await _lock.WaitAsync();
        try
        {
            return await action();
        }
        catch (TimeoutException e)
        {
            _logger.LogWarning(e, "SFU did not answer in time in hybrid session {SessionId}.", Model.Id);
            return timedOut;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        if (_gateway != null)
        {
            _gateway.ConnectionLost -= HandleGatewayConnectionLost;
            _gateway.Connected -= HandleGatewayConnected;
        }

        _lock.Dispose();
    }
}
