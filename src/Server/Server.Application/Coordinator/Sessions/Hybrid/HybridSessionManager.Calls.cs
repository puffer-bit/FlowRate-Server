using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Push;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Users;
using Server.Application.Mappers;
using Server.Core.Models.HybridSession;
using Server.Core.Models.TextChat;
using Shared.Enums;
using Shared.Enums.WebRTC;
using Shared.Models.DataTransferObjects;
using Shared.Models.DataTransferObjects.ChatSession.Messages;
using Shared.Models.Notifications.SessionInfo;
using Shared.Models.Requests.SessionActions.ChatSession;

namespace Server.Application.Coordinator.Sessions.Hybrid;

/// <summary>
/// Calls in direct chats and groups. A call lives while anyone is in it: Start opens it and joins the caller,
/// the other members ring for <see cref="RingTime"/> and join whenever they like, and the call ends when the
/// last member leaves. Media runs in a hybrid session under the chat ID that only the selected devices may join.
/// </summary>
public partial class HybridSessionManager
{
    public static readonly TimeSpan RingTime = TimeSpan.FromSeconds(45);

    private sealed class Participant(int userId)
    {
        public int UserId { get; } = userId;
        public ChatCallParticipantState State { get; set; }
        public DateTimeOffset RingsUntil { get; set; }
        // Tells a late expiry of an earlier ring from the current one
        public int Ring { get; set; }
    }

    private sealed class Call(Guid id, int chatId, int caller, DateTimeOffset created)
    {
        public Guid Id { get; } = id;
        public int ChatId { get; } = chatId;
        public int Caller { get; } = caller;
        public DateTimeOffset Created { get; } = created;
        public DateTimeOffset? Started { get; set; }
        public ChatCallState State { get; set; }
        public ChatCallEndReason? Reason { get; set; }
        public Dictionary<int, Participant> Participants { get; } = new();
        public ConcurrentDictionary<int, string> Devices { get; } = new();
        public IHybridSession? Session { get; set; }
        public Task PushTask { get; set; } = Task.CompletedTask;
        public bool IsJoined(int userId) => Devices.ContainsKey(userId);
        public bool Selected(int userId, string deviceId) => Devices.TryGetValue(userId, out var device) && device == deviceId;
        public bool Selected(string deviceId) => Devices.Values.Contains(deviceId);
    }

    private readonly SemaphoreSlim _callsLock = new(1, 1);
    private readonly ConcurrentDictionary<Guid, Call> _calls = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Func<CallPush, Task>? _callPush;
    private TimeProvider _time = TimeProvider.System;
    private IChatSessionManager? _callChats;
    private IUserManager? _callUsers;
    private IMessageSender? _callSender;
    private IConnectionsContext? _callConnections;

    public void ConfigureCalls(IChatSessionManager chats, IUserManager users, IMessageSender sender,
        IConnectionsContext connections, TimeProvider? timeProvider = null, Func<CallPush, Task>? push = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _callPush = push;
        _callChats = chats;
        _callUsers = users;
        _callSender = sender;
        _callConnections = connections;
    }

    public bool IsInCall(int userId) => _calls.Values.Any(c => c.State == ChatCallState.Active && c.IsJoined(userId));

    private Call? ActiveCallOf(int chatId) =>
        _calls.Values.FirstOrDefault(c => c.ChatId == chatId && c.State == ChatCallState.Active);

    public async Task<(ErrorCode? Error, ChatCallDTO? Call)> ChangeCallAsync(ChatCallRequest request,
        string connectionId)
    {
        await _callsLock.WaitAsync();
        try
        {
            if (_callChats == null || _callUsers == null) return (ErrorCode.FeatureDisabled, null);
            if (!_callChats.TryGetSession(request.ChatId, out var chat)) return (ErrorCode.ChatNotFound, null);
            if (chat.Kind is not (ChatKind.Direct or ChatKind.Group)) return (ErrorCode.WrongSessionType, null);
            if (!chat.MemberIds.Contains(request.SenderId)) return (ErrorCode.NotMember, null);
            if (request.CallId == Guid.Empty || !Enum.IsDefined(request.Action)) return (ErrorCode.InvalidValue, null);
            if (!_callConnections!.UserConnections.TryGetValue(request.SenderId, out var devices)
                || !devices.Contains(connectionId)) return (ErrorCode.NotConnected, null);

            if (!_calls.TryGetValue(request.CallId, out var call))
            {
                if (request.Action != ChatCallAction.Start) return (ErrorCode.CallNotFound, null);
                // Two members starting at once end up in one call
                if (ActiveCallOf(chat.Id) is { } running)
                {
                    var joinError = await JoinAsync(running, request.SenderId, connectionId);
                    return joinError != null ? (joinError, null) : (null, CallDto(running, connectionId));
                }
                var (startError, started) = await StartAsync(chat, request.CallId, request.SenderId, connectionId);
                if (startError != null) return (startError, null);
                call = started!;
            }
            else
            {
                if (call.ChatId != request.ChatId) return (ErrorCode.NotMember, null);
                if (call.State == ChatCallState.Ended) return (null, CallDto(call, connectionId));
                var error = request.Action switch
                {
                    ChatCallAction.Start or ChatCallAction.Join => await JoinAsync(call, request.SenderId, connectionId),
                    ChatCallAction.Decline => Decline(call, request.SenderId),
                    ChatCallAction.Leave => call.Selected(request.SenderId, connectionId)
                        ? await LeaveAsync(call, request.SenderId)
                        : ErrorCode.NotMember,
                    ChatCallAction.Ring => Ring(call, chat, request.SenderId, connectionId, request.TargetUserId),
                    _ => ErrorCode.InvalidValue
                };
                if (error != null) return (error, null);
            }
            // Retain recent tombstones to make retries harmless without unbounded growth.
            foreach (var old in _calls.Values.Where(c => c.State == ChatCallState.Ended)
                .OrderByDescending(c => c.Created).Skip(256).ToList()) _calls.TryRemove(old.Id, out _);
            return (null, CallDto(call, connectionId));
        }
        finally { _callsLock.Release(); }
    }

    private async Task<(ErrorCode? Error, Call? Call)> StartAsync(ChatSessionModel chat, Guid callId, int caller,
        string connectionId)
    {
        var call = new Call(callId, chat.Id, caller, _time.GetUtcNow());
        var model = new HybridSessionModel(chat.Id, _coordinatorInstanceId, chat.Name,
            Math.Max(2, chat.MemberIds.Count), WebRTCEngine.Mediasoup)
        { CallDevices = call.Devices };
        model.SetCallModerator(ModeratorOf(chat));
        var session = await CreateSessionAsync(model);
        if (model.State != HybridSessionNodeState.Active)
        {
            session.Dispose();
            return (ErrorCode.SFUUnavailable, null);
        }
        var leaveError = await LeaveOtherMediaAsync(caller, null);
        if (leaveError != null)
        {
            session.Dispose();
            return (leaveError, null);
        }
        // Record the start before announcing it; a failed write leaves no call behind.
        var error = await _callChats!.AddCallSystemMessageAsync(chat.Id, new ChatSystemContent
        { Kind = ChatSystemEventKind.CallStarted, ActorUserId = caller, CallId = callId });
        if (error != null)
        {
            session.Dispose();
            return (error, null);
        }
        _sessions[chat.Id] = session;
        call.Session = session;
        call.Devices[caller] = connectionId;
        call.Participants[caller] = new Participant(caller) { State = ChatCallParticipantState.Joined };
        _calls.TryAdd(call.Id, call);
        var rung = chat.MemberIds.Where(id => id != caller).Select(id => StartRing(call, id)).ToList();
        PublishCall(call, rung);
        return (null, call);
    }

    private async Task<ErrorCode?> JoinAsync(Call call, int userId, string connectionId)
    {
        if (call.Devices.TryGetValue(userId, out var selected))
        {
            if (selected == connectionId) return null;
            if (call.Session is { } current && current.PeerManager.HasPeer(userId))
            {
                var error = await ReleaseMediaPeerAsync(current, userId);
                if (error != null) return error;
            }
            call.Devices[userId] = connectionId;
            PublishCall(call, []);
            return null;
        }
        if (call.Session is { } session && session.Model.IsFull(call.Devices.Count)) return ErrorCode.SessionFull;
        var leaveError = await LeaveOtherMediaAsync(userId, call);
        if (leaveError != null) return leaveError;
        var wasRinging = call.Participants.TryGetValue(userId, out var participant)
            && participant.State == ChatCallParticipantState.Ringing;
        participant ??= call.Participants[userId] = new Participant(userId);
        participant.State = ChatCallParticipantState.Joined;
        participant.Ring++;
        call.Devices[userId] = connectionId;
        if (call.Started == null && call.Devices.Count > 1) call.Started = _time.GetUtcNow();
        PublishCall(call, wasRinging ? [participant] : []);
        return null;
    }

    private ErrorCode? Decline(Call call, int userId)
    {
        if (!call.Participants.TryGetValue(userId, out var participant)) return ErrorCode.NotMember;
        // A ring answered on another device can no longer be declined
        if (participant.State == ChatCallParticipantState.Joined) return ErrorCode.CallAnsweredElsewhere;
        if (participant.State != ChatCallParticipantState.Ringing) return null;
        participant.State = ChatCallParticipantState.Declined;
        participant.Ring++;
        PublishCall(call, [participant]);
        return null;
    }

    private ErrorCode? Ring(Call call, ChatSessionModel chat, int userId, string connectionId, int? targetId)
    {
        if (!call.Selected(userId, connectionId)) return ErrorCode.NotMember;
        if (targetId is { } target && (!chat.MemberIds.Contains(target) || call.IsJoined(target)))
            return ErrorCode.InvalidValue;
        var targets = targetId is { } single ? [single]
            : chat.MemberIds.Where(id => !call.IsJoined(id)).ToList();
        PublishCall(call, targets.Select(id => StartRing(call, id)).ToList());
        return null;
    }

    private Participant StartRing(Call call, int userId)
    {
        if (!call.Participants.TryGetValue(userId, out var participant))
            participant = call.Participants[userId] = new Participant(userId);
        participant.State = ChatCallParticipantState.Ringing;
        participant.RingsUntil = _time.GetUtcNow() + RingTime;
        var ring = ++participant.Ring;
        _ = ExpireRingAsync(call.Id, userId, ring);
        return participant;
    }

    /// <summary>Takes the user out of the call; the call ends with its last member.</summary>
    private async Task<ErrorCode?> LeaveAsync(Call call, int userId, int? kickedBy = null, string? reason = null,
        bool replaced = false)
    {
        if (call.Session is { } session && session.PeerManager.HasPeer(userId))
        {
            try
            {
                var error = kickedBy is { } initiator
                    ? await session.PeerManager.KickPeerAsync(initiator, userId, reason)
                    : replaced
                        ? await ReleaseMediaPeerAsync(session, userId)
                        : await session.PeerManager.RemovePeerAsync(userId);
                if (error is not (null or ErrorCode.PeerNotFound))
                {
                    _logger.LogWarning("Removing user {UserId} from call {CallId} failed: {Error}", userId, call.Id, error);
                    return error;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Removing user {UserId} from call {CallId} failed", userId, call.Id);
                return ErrorCode.Internal;
            }
        }
        call.Devices.TryRemove(userId, out _);
        if (call.Participants.TryGetValue(userId, out var participant))
            participant.State = ChatCallParticipantState.Left;
        if (call.Devices.IsEmpty) await FinishCallAsync(call, null, userId);
        else PublishCall(call, []);
        return null;
    }

    /// <summary>Joining a call leaves the voice channels and the other call the user was in.</summary>
    private async Task<ErrorCode?> LeaveOtherMediaAsync(int userId, Call? joining, int? joiningSessionId = null)
    {
        foreach (var other in _calls.Values.Where(c => c != joining && c.State == ChatCallState.Active
                     && c.IsJoined(userId)).ToList())
        {
            var error = await LeaveAsync(other, userId, replaced: true);
            if (error != null) return error;
        }
        foreach (var session in _sessions.Values.Where(s => s.Model.CallDevices == null
                     && s.Model.Id != joiningSessionId))
        {
            if (!session.PeerManager.HasPeer(userId)) continue;
            var error = await ReleaseMediaPeerAsync(session, userId);
            if (error is not (null or ErrorCode.PeerNotFound)) return error;
        }
        return null;
    }

    public async Task<ErrorCode?> DeclineCallFromPushAsync(Guid callId, int userId)
    {
        await _callsLock.WaitAsync();
        try
        {
            if (_callChats == null) return ErrorCode.FeatureDisabled;
            if (!_calls.TryGetValue(callId, out var call)) return ErrorCode.CallNotFound;
            if (!call.Participants.ContainsKey(userId)) return ErrorCode.NotMember;
            return call.State == ChatCallState.Ended ? null : Decline(call, userId);
        }
        finally { _callsLock.Release(); }
    }

    /// <summary>The selected device leaves the call through the media session.</summary>
    public async Task LeaveCallForSessionAsync(int sessionId, int userId, string connectionId)
    {
        await _callsLock.WaitAsync();
        try
        {
            if (ActiveCallOf(sessionId) is { } call && call.Selected(userId, connectionId))
                await LeaveAsync(call, userId);
        }
        finally { _callsLock.Release(); }
    }

    public async Task<ErrorCode?> KickFromCallAsync(int sessionId, int initiatorId, string connectionId, int userId,
        string? reason)
    {
        await _callsLock.WaitAsync();
        try
        {
            if (ActiveCallOf(sessionId) is not { } call) return ErrorCode.SessionNotFound;
            if (initiatorId == userId) return ErrorCode.CannotTargetSelf;
            if (call.Session?.Model.CallModeratorId != initiatorId || !call.Selected(initiatorId, connectionId))
                return ErrorCode.InsufficientPermissions;
            if (!call.IsJoined(userId)) return ErrorCode.PeerNotFound;
            return await LeaveAsync(call, userId, initiatorId, reason);
        }
        finally { _callsLock.Release(); }
    }

    /// <summary>Follows a change of the chat: removed members leave its call, the room fits the new size.</summary>
    public async Task OnChatChangedAsync(int chatId)
    {
        await _callsLock.WaitAsync();
        try
        {
            if (ActiveCallOf(chatId) is not { } call) return;
            if (!_callChats!.TryGetSession(chatId, out var chat))
            {
                await FinishCallAsync(call, null);
                return;
            }
            if (call.Session is { } session)
            {
                session.Model.SetCallModerator(ModeratorOf(chat));
                session.Model.ChangeCapacity(Math.Max(2, chat.MemberIds.Count));
            }
            var gone = call.Participants.Keys.Where(id => !chat.MemberIds.Contains(id)).ToList();
            if (gone.Count == 0)
            {
                PublishCall(call, []);
                return;
            }
            var stopped = new List<Participant>();
            foreach (var userId in gone)
            {
                var participant = call.Participants[userId];
                if (participant.State == ChatCallParticipantState.Ringing)
                {
                    participant.Ring++;
                    participant.State = ChatCallParticipantState.Missed;
                    stopped.Add(participant);
                }
                if (call.IsJoined(userId)) await LeaveAsync(call, userId);
                call.Participants.Remove(userId);
            }
            if (call.State == ChatCallState.Active) PublishCall(call, stopped);
        }
        finally { _callsLock.Release(); }
    }

    private static int? ModeratorOf(ChatSessionModel chat) => chat.Kind == ChatKind.Group ? chat.OwnerUserId : null;

    private ChatCallDTO CallDto(Call call, string device)
    {
        var selected = call.Selected(device);
        return new ChatCallDTO(call.Id, call.ChatId, call.Caller, call.State, call.Reason, call.Created, call.Started,
            call.Participants.Values.OrderBy(p => p.UserId)
                .Select(p => new ChatCallParticipantDTO(p.UserId, p.State)).ToList(),
            selected, call.State == ChatCallState.Active && selected && call.Session is { } session
                ? session.Model.ToDto() : null);
    }

    public async Task PublishCurrentCallAsync(int userId, string connectionId)
    {
        await _callsLock.WaitAsync();
        try
        {
            if (_callConnections == null || _callChats == null
                || !_callConnections.UserConnections.TryGetValue(userId, out var devices)
                || !devices.Contains(connectionId)) return;
            foreach (var call in _calls.Values.Where(c => c.State == ChatCallState.Active))
                if (_callChats.TryGetSession(call.ChatId, out var chat) && chat.MemberIds.Contains(userId))
                    _callSender!.SendMessageById(connectionId, new ChatCallChangedNotification(CallDto(call, connectionId)));
        }
        finally { _callsLock.Release(); }
    }

    private async Task PushCallAsync(Task previous, CallPush push)
    {
        try
        {
            await previous;
            if (_callPush != null) await _callPush(push);
        }
        catch (Exception ex) { _logger.LogError(ex, "Sending push for call {CallId} failed", push.Call.CallId); }
    }

    /// <param name="rings">Members whose ring started or stopped, for push notifications.</param>
    private void PublishCall(Call call, IReadOnlyCollection<Participant> rings)
    {
        ChatSessionModel? chat = null;
        _callChats?.TryGetSession(call.ChatId, out chat);
        // Preserve transition order without making signaling wait on an external push service.
        if (_callPush != null && rings.Count > 0)
        {
            var previous = call.PushTask;
            var push = new CallPush(CallDto(call, ""), chat?.Kind == ChatKind.Group ? chat.Name : null,
                rings.Select(p => new CallPushRecipient(p.UserId,
                    call.State == ChatCallState.Active && p.State == ChatCallParticipantState.Ringing,
                    p.RingsUntil)).ToList());
            call.PushTask = Task.Run(() => PushCallAsync(previous, push));
        }

        var audience = chat?.MemberIds.Union(call.Participants.Keys) ?? call.Participants.Keys;
        foreach (var userId in audience.ToList())
            if (_callConnections!.UserConnections.TryGetValue(userId, out var devices))
                foreach (var device in devices)
                    _callSender!.SendMessageById(device, new ChatCallChangedNotification(CallDto(call, device)));
    }

    /// <param name="reason">Disconnected when the server ends the call; otherwise worked out from the members.</param>
    private async Task FinishCallAsync(Call call, ChatCallEndReason? reason, int? actor = null)
    {
        if (call.State == ChatCallState.Ended) return;
        var rung = call.Participants.Values.Where(p => p.UserId != call.Caller).ToList();
        call.Reason = reason ?? (call.Started != null ? ChatCallEndReason.Completed
            : rung.Any(p => p.State == ChatCallParticipantState.Ringing) ? ChatCallEndReason.Cancelled
            : rung.Count > 0 && rung.All(p => p.State == ChatCallParticipantState.Declined) ? ChatCallEndReason.Declined
            : ChatCallEndReason.Missed);
        call.State = ChatCallState.Ended;
        var stopped = rung.Where(p => p.State == ChatCallParticipantState.Ringing).ToList();
        foreach (var participant in stopped)
        {
            participant.State = ChatCallParticipantState.Missed;
            participant.Ring++;
        }
        call.Devices.Clear();
        try
        {
            if (call.Session is { } session && _sessions.TryRemove(call.ChatId, out _))
            {
                try { await session.CloseSession(); }
                catch (Exception ex) { _logger.LogError(ex, "Closing media for call {CallId} failed", call.Id); }
                finally { session.Dispose(); }
            }
            var error = await _callChats!.AddCallSystemMessageAsync(call.ChatId, new ChatSystemContent
            {
                Kind = ChatSystemEventKind.CallEnded, ActorUserId = actor, CallId = call.Id,
                CallEndReason = call.Reason,
                CallDurationSeconds = call.Reason == ChatCallEndReason.Completed && call.Started is { } started
                    ? (int)Math.Max(0, (_time.GetUtcNow() - started).TotalSeconds) : null
            });
            if (error != null) _logger.LogError("Recording end of call {CallId} failed: {Error}", call.Id, error);
        }
        finally { PublishCall(call, stopped); }
    }

    private async Task ExpireRingAsync(Guid id, int userId, int ring)
    {
        try
        {
            await Task.Delay(RingTime, _time, _lifetime.Token);
            await _callsLock.WaitAsync(_lifetime.Token);
            try
            {
                if (_calls.TryGetValue(id, out var call) && call.State == ChatCallState.Active
                    && call.Participants.TryGetValue(userId, out var participant)
                    && participant.Ring == ring && participant.State == ChatCallParticipantState.Ringing)
                {
                    participant.State = ChatCallParticipantState.Missed;
                    PublishCall(call, [participant]);
                }
            }
            finally { _callsLock.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogError(ex, "Expiring a ring of call {CallId} failed", id); }
    }

    /// <summary>A device that goes away leaves the calls it takes part in; null means every device of the user.</summary>
    private async Task LeaveCallsOfDeviceAsync(int userId, string? device)
    {
        await _callsLock.WaitAsync();
        try
        {
            foreach (var call in _calls.Values.Where(c => c.State == ChatCallState.Active && c.IsJoined(userId)).ToList())
                if (device == null || call.Selected(userId, device))
                    await LeaveAsync(call, userId);
        }
        finally { _callsLock.Release(); }
    }

    /// <summary>A voice channel taken leaves the call the user was in.</summary>
    public Task LeaveCallsAsync(int userId) => LeaveCallsOfDeviceAsync(userId, null);

    private async Task EndAllCallsAsync()
    {
        await _callsLock.WaitAsync();
        try
        {
            foreach (var call in _calls.Values.Where(c => c.State == ChatCallState.Active).ToList())
                await FinishCallAsync(call, ChatCallEndReason.Disconnected);
        }
        finally { _callsLock.Release(); }
    }
}
