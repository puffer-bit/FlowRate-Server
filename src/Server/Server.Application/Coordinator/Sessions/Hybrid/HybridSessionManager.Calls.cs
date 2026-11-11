using Microsoft.Extensions.Logging;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Users;
using Server.Application.Mappers;
using Server.Core.Models.HybridSession;
using Shared.Enums;
using Shared.Enums.WebRTC;
using Shared.Models.DataTransferObjects;
using Shared.Models.DataTransferObjects.ChatSession.Messages;
using Shared.Models.Notifications.SessionInfo;
using Shared.Models.Requests.SessionActions.ChatSession;

namespace Server.Application.Coordinator.Sessions.Hybrid;

public partial class HybridSessionManager
{
    private sealed class Call(Guid id, int chatId, int caller, int callee, string device, DateTimeOffset created)
    {
        public Guid Id { get; } = id;
        public int ChatId { get; } = chatId;
        public int Caller { get; } = caller;
        public int Callee { get; } = callee;
        public string CallerDevice { get; } = device;
        public string? CalleeDevice { get; set; }
        public DateTimeOffset Created { get; } = created;
        public DateTimeOffset? Started { get; set; }
        public DirectCallState State { get; set; }
        public DirectCallEndReason? Reason { get; set; }
        public Task PushTask { get; set; } = Task.CompletedTask;
        public bool Contains(int userId) => userId == Caller || userId == Callee;
        public bool Selected(string deviceId) => deviceId == CallerDevice || deviceId == CalleeDevice;
    }

    private readonly SemaphoreSlim _callsLock = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Call> _calls = new();
    private readonly CancellationTokenSource _callLifetime = new();
    private Func<DirectCallDTO, Task>? _callPush;
    private TimeProvider _callTime = TimeProvider.System;
    private IChatSessionManager? _callChats;
    private IUserManager? _callUsers;
    private IMessageSender? _callSender;
    private IConnectionsContext? _callConnections;

    public void ConfigureCalls(IChatSessionManager chats, IUserManager users, IMessageSender sender,
        IConnectionsContext connections, TimeProvider? timeProvider = null, Func<DirectCallDTO, Task>? push = null)
    {
        _callTime = timeProvider ?? TimeProvider.System;
        _callPush = push;
        _callChats = chats;
        _callUsers = users;
        _callSender = sender;
        _callConnections = connections;
    }

    public bool HasDirectCall(int userId) => _calls.Values.Any(c => c.Contains(userId) && c.State != DirectCallState.Ended);

    public async Task<(ErrorCode? Error, DirectCallDTO? Call)> ChangeCallAsync(DirectCallRequest request,
        string connectionId)
    {
        await _callsLock.WaitAsync();
        try
        {
            if (_callChats == null || _callUsers == null) return (ErrorCode.FeatureDisabled, null);
            if (!_callChats.TryGetSession(request.ChatId, out var chat)) return (ErrorCode.ChatNotFound, null);
            if (chat.Kind != ChatKind.Direct) return (ErrorCode.WrongSessionType, null);
            if (!chat.MemberIds.Contains(request.SenderId)) return (ErrorCode.NotMember, null);
            if (request.CallId == Guid.Empty) return (ErrorCode.InvalidValue, null);
            if (!Enum.IsDefined(request.Action)) return (ErrorCode.InvalidValue, null);
            if (!_callConnections!.UserConnections.TryGetValue(request.SenderId, out var devices)
                || !devices.Contains(connectionId)) return (ErrorCode.NotConnected, null);

            if (!_calls.TryGetValue(request.CallId, out var call))
            {
                if (request.Action != DirectCallAction.Start) return (ErrorCode.SessionNotFound, null);
                var other = chat.MemberIds.Single(id => id != request.SenderId);
                if (_calls.Values.Any(c => c.State != DirectCallState.Ended &&
                    (c.Contains(request.SenderId) || c.Contains(other)))) return (ErrorCode.Conflict, null);
                var leaveError = await LeaveVoiceChannelsForCallAsync(request.SenderId);
                if (leaveError != null) return (leaveError, null);
                call = new Call(request.CallId, chat.Id, request.SenderId, other, connectionId, _callTime.GetUtcNow());
                _calls.TryAdd(call.Id, call);
                PublishCall(call);
                _ = ExpireCallAsync(call.Id);
            }
            else
            {
                if (call.ChatId != request.ChatId || !call.Contains(request.SenderId)) return (ErrorCode.NotMember, null);
                if (request.Action == DirectCallAction.Start && connectionId != call.CallerDevice)
                    return (ErrorCode.NotMember, null);
                if (call.State == DirectCallState.Ringing && _callTime.GetUtcNow() >= call.Created.AddSeconds(45))
                    await FinishCallAsync(call, DirectCallEndReason.Missed);
                if (call.State == DirectCallState.Ended) return (null, CallDto(call, connectionId));
                switch (request.Action)
                {
                    case DirectCallAction.Accept:
                        if (request.SenderId != call.Callee) return (ErrorCode.NotMember, null);
                        if (call.State == DirectCallState.Active)
                            return call.CalleeDevice == connectionId ? (null, CallDto(call, connectionId)) : (ErrorCode.Conflict, null);
                        var model = new HybridSessionModel(call.ChatId, _coordinatorInstanceId, chat.Name, 2,
                            call.Caller, WebRTCEngine.Mediasoup)
                        {
                            CallDevices = new Dictionary<int, string>
                            { [call.Caller] = call.CallerDevice, [call.Callee] = connectionId }
                        };
                        var session = await CreateSessionAsync(model);
                        if (model.State != HybridSessionNodeState.Active)
                        {
                            session.Dispose();
                            return (ErrorCode.SFUUnavailable, null);
                        }
                        var callerLeaveError = await LeaveVoiceChannelsForCallAsync(call.Caller);
                        var calleeLeaveError = callerLeaveError == null
                            ? await LeaveVoiceChannelsForCallAsync(call.Callee) : null;
                        if (callerLeaveError != null || calleeLeaveError != null)
                        {
                            session.Dispose();
                            return (callerLeaveError ?? calleeLeaveError, null);
                        }
                        // Record the transition before announcing it; a failed write leaves the invitation pending.
                        var error = await _callChats.AddCallSystemMessageAsync(call.ChatId, new ChatSystemContent
                        { Kind = ChatSystemEventKind.CallStarted, ActorUserId = call.Caller, CallId = call.Id });
                        if (error != null) { session.Dispose(); return (error, null); }
                        _sessions[call.ChatId] = session;
                        call.CalleeDevice = connectionId;
                        call.Started = _callTime.GetUtcNow();
                        call.State = DirectCallState.Active;
                        PublishCall(call);
                        break;
                    case DirectCallAction.Decline:
                        if (request.SenderId != call.Callee || call.State != DirectCallState.Ringing)
                            return (ErrorCode.Conflict, null);
                        await FinishCallAsync(call, DirectCallEndReason.Declined, request.SenderId);
                        break;
                    case DirectCallAction.End:
                        if (!call.Selected(connectionId)) return (ErrorCode.NotMember, null);
                        await FinishCallAsync(call, call.State == DirectCallState.Ringing
                            ? DirectCallEndReason.Cancelled : DirectCallEndReason.Completed, request.SenderId);
                        break;
                }
            }
            // Retain recent tombstones to make retries harmless without unbounded growth.
            foreach (var old in _calls.Values.Where(c => c.State == DirectCallState.Ended)
                .OrderByDescending(c => c.Created).Skip(256).ToList()) _calls.TryRemove(old.Id, out _);
            return (null, CallDto(call, connectionId));
        }
        finally { _callsLock.Release(); }
    }

    private async Task<ErrorCode?> LeaveVoiceChannelsForCallAsync(int userId)
    {
        foreach (var session in _sessions.Values.Where(s => s.Model.CallDevices == null))
        {
            if (!session.PeerManager.HasPeer(userId)) continue;
            var error = await session.PeerManager.RemovePeerAsync(userId);
            if (error is not (null or ErrorCode.PeerNotFound)) return error;
        }
        return null;
    }

    private DirectCallDTO CallDto(Call call, string device) => new(call.Id, call.ChatId, call.Caller, call.Callee,
        call.State, call.Reason, call.Created, call.Started, call.Selected(device),
        call.State == DirectCallState.Active && call.Selected(device)
            && _sessions.TryGetValue(call.ChatId, out var session) ? session.Model.ToDto() : null);

    public async Task PublishCurrentCallAsync(int userId, string connectionId)
    {
        await _callsLock.WaitAsync();
        try
        {
            if (_callConnections == null || !_callConnections.UserConnections.TryGetValue(userId, out var devices)
                || !devices.Contains(connectionId)) return;
            foreach (var call in _calls.Values.Where(c => c.Contains(userId) && c.State != DirectCallState.Ended))
                _callSender!.SendMessageById(connectionId, new DirectCallChangedNotification(CallDto(call, connectionId)));
        }
        finally { _callsLock.Release(); }
    }

    private async Task PushCallAsync(Task previous, DirectCallDTO call)
    {
        try
        {
            await previous;
            if (_callPush != null) await _callPush(call);
        }
        catch (Exception ex) { _logger.LogError(ex, "Sending push for call {CallId} failed", call.CallId); }
    }

    private void PublishCall(Call call)
    {
        // Preserve transition order without making signaling wait on an external push service.
        if (_callPush != null)
        {
            var previous = call.PushTask;
            var snapshot = CallDto(call, "");
            call.PushTask = Task.Run(() => PushCallAsync(previous, snapshot));
        }

        foreach (var userId in new[] { call.Caller, call.Callee })
            if (_callConnections!.UserConnections.TryGetValue(userId, out var devices))
                foreach (var device in devices)
                    _callSender!.SendMessageById(device, new DirectCallChangedNotification(CallDto(call, device)));
    }

    private async Task FinishCallAsync(Call call, DirectCallEndReason reason, int? actor = null)
    {
        if (call.State == DirectCallState.Ended) return;
        var wasActive = call.State == DirectCallState.Active;
        call.State = DirectCallState.Ended;
        call.Reason = reason;
        try
        {
            if (_sessions.TryRemove(call.ChatId, out var session))
            {
                try { await session.CloseSession(); }
                catch (Exception ex) { _logger.LogError(ex, "Closing media for call {CallId} failed", call.Id); }
                finally { session.Dispose(); }
            }
            var error = await _callChats!.AddCallSystemMessageAsync(call.ChatId, new ChatSystemContent
            {
                Kind = ChatSystemEventKind.CallEnded, ActorUserId = actor, CallId = call.Id,
                CallEndReason = reason,
                CallDurationSeconds = wasActive && call.Started is { } started
                    ? (int)Math.Max(0, (_callTime.GetUtcNow() - started).TotalSeconds) : null
            });
            if (error != null) _logger.LogError("Recording end of call {CallId} failed: {Error}", call.Id, error);
        }
        finally { PublishCall(call); }
    }

    private async Task ExpireCallAsync(Guid id)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), _callTime, _callLifetime.Token);
            await _callsLock.WaitAsync(_callLifetime.Token);
            try
            {
                if (_calls.TryGetValue(id, out var call) && call.State == DirectCallState.Ringing)
                    await FinishCallAsync(call, DirectCallEndReason.Missed);
            }
            finally { _callsLock.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogError(ex, "Expiring call {CallId} failed", id); }
    }

    private async Task EndCallsForDeviceAsync(int userId, string? device)
    {
        await _callsLock.WaitAsync();
        try
        {
            foreach (var call in _calls.Values.Where(c => c.State != DirectCallState.Ended && c.Contains(userId)).ToList())
                if (call.Selected(device ?? "") || (device == null
                    && (call.Caller == userId || call.State == DirectCallState.Active)))
                    await FinishCallAsync(call, DirectCallEndReason.Disconnected);
        }
        finally { _callsLock.Release(); }
    }

    public async Task EndCallForSessionAsync(int sessionId, int userId, string connectionId)
    {
        await _callsLock.WaitAsync();
        try
        {
            var call = _calls.Values.FirstOrDefault(c => c.ChatId == sessionId && c.State == DirectCallState.Active);
            if (call != null && call.Contains(userId) && call.Selected(connectionId))
                await FinishCallAsync(call, DirectCallEndReason.Completed, userId);
        }
        finally { _callsLock.Release(); }
    }

    private async Task EndAllCallsAsync()
    {
        await _callsLock.WaitAsync();
        try
        {
            foreach (var call in _calls.Values.Where(c => c.State != DirectCallState.Ended).ToList())
                await FinishCallAsync(call, DirectCallEndReason.Disconnected);
        }
        finally { _callsLock.Release(); }
    }
}
