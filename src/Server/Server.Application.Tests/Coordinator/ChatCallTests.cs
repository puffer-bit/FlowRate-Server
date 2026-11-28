using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Server.Application.Abstractions.Factories;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Users;
using Server.Application.Orchestrator.SFUPool;
using Server.Core.Models.HybridSession;
using Server.Core.Models.TextChat;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Models;
using Shared.Models.DataTransferObjects;
using Shared.Models.DataTransferObjects.ChatSession.Messages;
using Shared.Models.Notifications.SessionInfo;
using Shared.Models.Requests.SessionActions.ChatSession;

namespace Server.Application.Tests.Coordinator;

public sealed class ChatCallTests : IDisposable
{
    private readonly Mock<IChatSessionManager> _chats = new();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<IVideoSessionFactory> _factory = new();
    private readonly Mock<IHybridSession> _session = new();
    private readonly Mock<IHybridSessionPeerManager> _peers = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ConcurrentQueue<(string Device, ChatCallDTO Call)> _sent = new();
    private readonly ConcurrentQueue<ChatSystemContent> _history = new();
    private readonly HybridSessionManager _manager;
    private readonly Mock<IVideoSessionRepository> _repository = new();
    private bool _sfuReady = true;
    private readonly ConcurrentDictionary<int, string[]> _devices = new();

    private readonly ChatSessionModel _group;
    private readonly Mock<IMessageSender> _sender = new();
    private readonly List<Server.Application.Coordinator.Push.CallPush> _pushes = new();

    public ChatCallTests()
    {
        var chat = ChatSessionModel.Create("server", "Direct", 1);
        chat.Id = 7;
        chat.SetAudience(ChatKind.Direct, new[] { 1, 2 });
        _chats.Setup(c => c.TryGetSession(7, out chat)).Returns(true);
        _group = ChatSessionModel.Create("server", "Friends", 1);
        _group.Id = 8;
        _group.ChangeAdmin(1);
        _group.SetAudience(ChatKind.Group, new[] { 1, 2, 3 });
        var group = _group;
        _chats.Setup(c => c.TryGetSession(8, out group)).Returns(true);
        _chats.Setup(c => c.AddCallSystemMessageAsync(It.IsAny<int>(), It.IsAny<ChatSystemContent>()))
            .Callback((int _, ChatSystemContent content) => _history.Enqueue(content))
            .ReturnsAsync((ErrorCode?)null);
        UserModel? online = null;
        _users.Setup(u => u.TryGetConnectedUser(It.IsAny<int>(), out online)).Returns(true);
        _session.Setup(s => s.PeerManager).Returns(_peers.Object);
        _peers.Setup(p => p.GetAllPeerSnapshots()).Returns(new Dictionary<int, PeerSnapshot>());
        _factory.Setup(f => f.CreateHybridSession(It.IsAny<HybridSessionModel>(), It.IsAny<ICoordinatorEventBus>(),
                It.IsAny<ISFUNodeSelector>()))
            .Returns((HybridSessionModel model, ICoordinatorEventBus _, ISFUNodeSelector _) =>
            {
                _session.Setup(s => s.Model).Returns(model);
                _session.Setup(s => s.AssignSfuNodeAsync()).Callback(() =>
                    model.SetState(_sfuReady ? HybridSessionNodeState.Active : HybridSessionNodeState.WaitingForSFUNode))
                    .Returns(Task.CompletedTask);
                return _session.Object;
            });
        var connections = new Mock<IConnectionsContext>();
        _devices[1] = ["caller", "caller-other"];
        _devices[2] = ["callee", "callee-other"];
        _devices[3] = ["outsider"];
        connections.Setup(c => c.UserConnections).Returns(_devices);
        _sender.Setup(s => s.SendMessageById(It.IsAny<string>(), It.IsAny<BaseMessage>()))
            .Callback((string device, BaseMessage message) =>
            {
                if (message is ChatCallChangedNotification changed) _sent.Enqueue((device, changed.Call));
            });
        _manager = new HybridSessionManager(NullLogger.Instance, Mock.Of<ICoordinatorEventBus>(), _factory.Object,
            Mock.Of<ISFUNodeSelector>(), _repository.Object, "server");
        _manager.ConfigureCalls(_chats.Object, _users.Object, _sender.Object, connections.Object, _time, push =>
        {
            lock (_pushes) _pushes.Add(push);
            return Task.CompletedTask;
        });
    }

    private Task<(ErrorCode? Error, ChatCallDTO? Call)> Act(Guid id, ChatCallAction action,
        int user = 1, string device = "caller", int chat = 7, int? target = null) =>
        _manager.ChangeCallAsync(new ChatCallRequest(chat, id, action, target) { SenderId = user }, device);

    private static ChatCallParticipantState? StateOf(ChatCallDTO call, int user) =>
        call.Participants.FirstOrDefault(p => p.UserId == user)?.State;

    private async Task<List<Server.Application.Coordinator.Push.CallPush>> PushesAsync(int count)
    {
        for (var i = 0; i < 200; i++)
        {
            lock (_pushes)
                if (_pushes.Count >= count) return _pushes.ToList();
            await Task.Delay(25);
        }
        throw new TimeoutException("The pushes never came.");
    }

    private async Task<ChatCallDTO> WaitForAsync(Func<ChatCallDTO, bool> condition)
    {
        for (var i = 0; i < 200; i++)
        {
            if (_sent.LastOrDefault(item => condition(item.Call)) is { Call: { } call }) return call;
            await Task.Delay(25);
        }
        throw new TimeoutException("The call never reached the expected state.");
    }

    private async Task<Mock<IHybridSessionPeerManager>> AddVoiceChannelAsync(params int[] users)
    {
        var members = users.ToHashSet();
        var peers = new Mock<IHybridSessionPeerManager>();
        peers.Setup(p => p.HasPeer(It.IsAny<int>())).Returns((int user) => members.Contains(user));
        peers.Setup(p => p.GetAllPeerSnapshots()).Returns(new Dictionary<int, PeerSnapshot>());
        peers.Setup(p => p.AddPeerAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), false, false))
            .Callback((int user, string _, string _, bool _, bool _) => members.Add(user))
            .ReturnsAsync((null, null));
        peers.Setup(p => p.RemoveReplacedPeerAsync(It.IsAny<int>()))
            .Callback((int user) => members.Remove(user)).ReturnsAsync((ErrorCode?)null);
        var model = HybridSessionModel.Create("server", "Voice", 10, Shared.Enums.WebRTC.WebRTCEngine.Mediasoup);
        model.Id = 20;
        var channel = new Mock<IHybridSession>();
        channel.Setup(s => s.Model).Returns(model);
        channel.Setup(s => s.PeerManager).Returns(peers.Object);
        channel.Setup(s => s.AssignSfuNodeAsync()).Returns(Task.CompletedTask);
        _factory.Setup(f => f.CreateHybridSession(model, It.IsAny<ICoordinatorEventBus>(), It.IsAny<ISFUNodeSelector>()))
            .Returns(channel.Object);
        _repository.Setup(r => r.GetByCoordinatorIdAsync("server", default)).ReturnsAsync(new List<HybridSessionModel> { model });
        await _manager.LoadAsync();
        return peers;
    }

    [Fact]
    public async Task Start_JoinsTheCallerAtOnce_AndRingsTheOthers()
    {
        var id = Guid.NewGuid();
        var (error, call) = await Act(id, ChatCallAction.Start);
        Assert.Null(error);
        Assert.Equal(ChatCallState.Active, call!.State);
        Assert.True(call.IsSelectedDevice);
        Assert.NotNull(call.Session);
        Assert.Null(call.StartedAt);
        Assert.Equal(ChatCallParticipantState.Joined, StateOf(call, 1));
        Assert.Equal(ChatCallParticipantState.Ringing, StateOf(call, 2));
        Assert.True(_manager.TryGetSession(7, out _));
        var started = Assert.Single(_history);
        Assert.Equal(ChatSystemEventKind.CallStarted, started.Kind);
        Assert.True(started.IsValid());
        // Every device of the members hears of it, only the selected one gets the session
        Assert.Contains(_sent, s => s.Device == "callee" && s.Call.Session == null && !s.Call.IsSelectedDevice);
        Assert.Contains(_sent, s => s.Device == "caller-other" && s.Call.Session == null);
        Assert.DoesNotContain(_sent, s => s.Device == "outsider");
        var push = Assert.Single(await PushesAsync(1));
        Assert.Null(push.GroupName);
        Assert.Equal(2, Assert.Single(push.Recipients, r => r.IsRinging).UserId);
    }

    [Fact]
    public async Task Join_StartsTheClock_AndTheCallEndsWithItsLastMember()
    {
        var id = Guid.NewGuid();
        await Act(id, ChatCallAction.Start);
        _time.Advance(TimeSpan.FromSeconds(5));
        var joined = (await Act(id, ChatCallAction.Join, 2, "callee")).Call!;
        Assert.NotNull(joined.StartedAt);
        Assert.Equal(ChatCallParticipantState.Joined, StateOf(joined, 2));
        Assert.True(_manager.IsInCall(2));
        _time.Advance(TimeSpan.FromSeconds(65));
        var left = (await Act(id, ChatCallAction.Leave)).Call!;
        Assert.Equal(ChatCallState.Active, left.State);
        Assert.Equal(ChatCallParticipantState.Left, StateOf(left, 1));
        Assert.True(_manager.TryGetSession(7, out _));
        var ended = (await Act(id, ChatCallAction.Leave, 2, "callee")).Call!;
        Assert.Equal(ChatCallState.Ended, ended.State);
        Assert.Equal(ChatCallEndReason.Completed, ended.EndReason);
        Assert.False(_manager.TryGetSession(7, out _));
        var record = _history.Last();
        Assert.Equal(65, record.CallDurationSeconds);
        Assert.True(record.IsValid());
    }

    [Fact]
    public async Task LoneCaller_StaysAfterTheRingTimesOut_AndTheCallIsMissed()
    {
        var id = Guid.NewGuid();
        await Act(id, ChatCallAction.Start);
        _time.Advance(HybridSessionManager.RingTime);
        var missed = await WaitForAsync(c => StateOf(c, 2) == ChatCallParticipantState.Missed);
        Assert.Equal(ChatCallState.Active, missed.State);
        Assert.Contains(await PushesAsync(2), p => p.Recipients.Any(r => r.UserId == 2 && !r.IsRinging));
        // A late member still joins
        Assert.Equal(ChatCallParticipantState.Joined, StateOf((await Act(id, ChatCallAction.Join, 2, "callee")).Call!, 2));
        await Act(id, ChatCallAction.Leave, 2, "callee");
        var ended = (await Act(id, ChatCallAction.Leave)).Call!;
        Assert.Equal(ChatCallEndReason.Completed, ended.EndReason);
    }

    [Theory]
    [InlineData(false, ChatCallEndReason.Cancelled)]
    [InlineData(true, ChatCallEndReason.Declined)]
    public async Task UnansweredCall_StoresItsOutcome(bool decline, ChatCallEndReason reason)
    {
        var id = Guid.NewGuid();
        await Act(id, ChatCallAction.Start);
        if (decline)
        {
            var declined = (await Act(id, ChatCallAction.Decline, 2, "callee")).Call!;
            Assert.Equal(ChatCallState.Active, declined.State);
            Assert.Equal(ChatCallParticipantState.Declined, StateOf(declined, 2));
        }
        var ended = (await Act(id, ChatCallAction.Leave)).Call!;
        Assert.Equal(reason, ended.EndReason);
        var record = _history.Last();
        Assert.Equal(reason, record.CallEndReason);
        Assert.Null(record.CallDurationSeconds);
        Assert.True(record.IsValid());
    }

    [Fact]
    public async Task MissedRing_EndsAsMissed()
    {
        var id = Guid.NewGuid();
        await Act(id, ChatCallAction.Start);
        _time.Advance(HybridSessionManager.RingTime);
        await WaitForAsync(c => StateOf(c, 2) == ChatCallParticipantState.Missed);
        Assert.Equal(ChatCallEndReason.Missed, (await Act(id, ChatCallAction.Leave)).Call!.EndReason);
    }

    [Fact]
    public async Task DeclineFromPush_DeclinesOnlyTheRingOfThatMember()
    {
        var id = Guid.NewGuid();
        await Act(id, ChatCallAction.Start);
        Assert.Equal(ErrorCode.NotMember, await _manager.DeclineCallFromPushAsync(id, 3));
        Assert.Equal(ErrorCode.CallNotFound, await _manager.DeclineCallFromPushAsync(Guid.NewGuid(), 2));
        Assert.Null(await _manager.DeclineCallFromPushAsync(id, 2));
        Assert.Contains(_sent, s => s.Device == "caller" && StateOf(s.Call, 2) == ChatCallParticipantState.Declined);
        Assert.True(_manager.IsInCall(1));
        await Act(id, ChatCallAction.Join, 2, "callee");
        Assert.Equal(ErrorCode.CallAnsweredElsewhere, await _manager.DeclineCallFromPushAsync(id, 2));
    }

    [Fact]
    public async Task OneDevicePerMember()
    {
        var id = Guid.NewGuid();
        await Act(id, ChatCallAction.Start);
        await Act(id, ChatCallAction.Join, 2, "callee");
        _peers.Setup(p => p.HasPeer(2)).Returns(true);
        Assert.Null((await Act(id, ChatCallAction.Join, 2, "callee-other")).Error);
        _peers.Verify(p => p.RemoveReplacedPeerAsync(2), Times.Once);
        Assert.Contains(_sent, s => s.Device == "callee" && !s.Call.IsSelectedDevice);
        Assert.True(_sent.Last(s => s.Device == "callee-other").Call.IsSelectedDevice);
        Assert.Equal(ErrorCode.NotMember, (await Act(id, ChatCallAction.Leave, 2, "callee")).Error);
        Assert.Null((await Act(id, ChatCallAction.Join, 2, "callee-other")).Error);
    }

    [Fact]
    public async Task CompetingStarts_EndUpInOneCall()
    {
        var first = Guid.NewGuid();
        await Act(first, ChatCallAction.Start);
        var second = (await Act(Guid.NewGuid(), ChatCallAction.Start, 2, "callee")).Call!;
        Assert.Equal(first, second.CallId);
        Assert.Equal(ChatCallParticipantState.Joined, StateOf(second, 2));
        Assert.Single(_history);
    }

    [Fact]
    public async Task Ring_CallsAMemberAgain_OnlyFromInsideTheCall()
    {
        var id = Guid.NewGuid();
        await Act(id, ChatCallAction.Start);
        await Act(id, ChatCallAction.Decline, 2, "callee");
        Assert.Equal(ErrorCode.NotMember, (await Act(id, ChatCallAction.Ring, 2, "callee", target: 1)).Error);
        Assert.Equal(ErrorCode.InvalidValue, (await Act(id, ChatCallAction.Ring, target: 1)).Error);
        Assert.Equal(ErrorCode.InvalidValue, (await Act(id, ChatCallAction.Ring, target: 3)).Error);
        var rung = (await Act(id, ChatCallAction.Ring, target: 2)).Call!;
        Assert.Equal(ChatCallParticipantState.Ringing, StateOf(rung, 2));
        Assert.True((await PushesAsync(3)).Last().Recipients.Single().IsRinging);
        // The earlier ring timing out does not end the new one
        _time.Advance(TimeSpan.FromSeconds(30));
        await Act(id, ChatCallAction.Decline, 2, "callee");
        await Act(id, ChatCallAction.Ring);
        _time.Advance(TimeSpan.FromSeconds(20));
        await Task.Delay(100);
        Assert.Equal(ChatCallParticipantState.Ringing, StateOf(_sent.Last().Call, 2));
    }

    [Fact]
    public async Task GroupCall_RingsEveryone_AndLateMembersJoin()
    {
        var id = Guid.NewGuid();
        var call = (await Act(id, ChatCallAction.Start, chat: 8)).Call!;
        Assert.Equal(ChatCallParticipantState.Ringing, StateOf(call, 2));
        Assert.Equal(ChatCallParticipantState.Ringing, StateOf(call, 3));
        Assert.Equal("Friends", Assert.Single(await PushesAsync(1)).GroupName);
        Assert.True(_manager.TryGetSession(8, out var session));
        Assert.Equal(3, session!.Model.Capacity);
        Assert.Equal(1, session.Model.CallModeratorId);
        await Act(id, ChatCallAction.Decline, 2, "callee", chat: 8);
        await Act(id, ChatCallAction.Join, 3, "outsider", chat: 8);
        // Declining does not keep one out
        var all = (await Act(id, ChatCallAction.Join, 2, "callee", chat: 8)).Call!;
        Assert.All(all.Participants, p => Assert.Equal(ChatCallParticipantState.Joined, p.State));
        await Act(id, ChatCallAction.Leave, chat: 8);
        Assert.Equal(ChatCallState.Active, (await Act(id, ChatCallAction.Leave, 2, "callee", chat: 8)).Call!.State);
        Assert.Equal(ChatCallEndReason.Completed, (await Act(id, ChatCallAction.Leave, 3, "outsider", chat: 8)).Call!.EndReason);
    }

    [Fact]
    public async Task GroupOwner_KicksFromTheCall_AndTheMemberMayComeBack()
    {
        var id = Guid.NewGuid();
        await Act(id, ChatCallAction.Start, chat: 8);
        await Act(id, ChatCallAction.Join, 2, "callee", chat: 8);
        await Act(id, ChatCallAction.Join, 3, "outsider", chat: 8);
        _peers.Setup(p => p.HasPeer(It.IsAny<int>())).Returns(true);
        _peers.Setup(p => p.KickPeerAsync(1, 3, "bye")).ReturnsAsync((ErrorCode?)null);
        Assert.Equal(ErrorCode.InsufficientPermissions, await _manager.KickFromCallAsync(8, 2, "callee", 3, null));
        Assert.Equal(ErrorCode.InsufficientPermissions, await _manager.KickFromCallAsync(8, 1, "caller-other", 3, null));
        Assert.Equal(ErrorCode.CannotTargetSelf, await _manager.KickFromCallAsync(8, 1, "caller", 1, null));
        Assert.Null(await _manager.KickFromCallAsync(8, 1, "caller", 3, "bye"));
        _peers.Verify(p => p.KickPeerAsync(1, 3, "bye"), Times.Once);
        Assert.Equal(ChatCallParticipantState.Left, StateOf(_sent.Last().Call, 3));
        Assert.False(_manager.IsInCall(3));
        Assert.Null((await Act(id, ChatCallAction.Join, 3, "outsider", chat: 8)).Error);
    }

    [Fact]
    public async Task DirectCall_HasNoModerator()
    {
        var id = Guid.NewGuid();
        await Act(id, ChatCallAction.Start);
        await Act(id, ChatCallAction.Join, 2, "callee");
        Assert.Equal(ErrorCode.InsufficientPermissions, await _manager.KickFromCallAsync(7, 1, "caller", 2, null));
    }

    [Fact]
    public async Task RemovedGroupMember_LeavesTheCall()
    {
        var id = Guid.NewGuid();
        await Act(id, ChatCallAction.Start, chat: 8);
        await Act(id, ChatCallAction.Join, 3, "outsider", chat: 8);
        _group.SetAudience(ChatKind.Group, new[] { 1, 2 });
        await _manager.OnChatChangedAsync(8);
        var call = _sent.Last().Call;
        Assert.Null(StateOf(call, 3));
        Assert.False(_manager.IsInCall(3));
        Assert.True(_manager.TryGetSession(8, out var session));
        Assert.Equal(2, session!.Model.Capacity);
    }

    [Fact]
    public async Task Errors_ForStrangersAndUnknownCalls()
    {
        var id = Guid.NewGuid();
        Assert.Equal(ErrorCode.NotMember, (await Act(id, ChatCallAction.Start, 3, "outsider")).Error);
        Assert.Equal(ErrorCode.CallNotFound, (await Act(id, ChatCallAction.Join, 2, "callee")).Error);
        await Act(id, ChatCallAction.Start);
        Assert.Equal(ErrorCode.NotConnected, (await Act(id, ChatCallAction.Join, 2, "caller")).Error);
        Assert.Equal(ErrorCode.NotMember, (await Act(id, ChatCallAction.Leave, 1, "caller-other")).Error);
        Assert.Equal(ErrorCode.NotMember, (await Act(id, ChatCallAction.Join, 3, "outsider")).Error);
    }

    [Fact]
    public async Task JoiningLeavesTheVoiceChannel_AndAChannelLeavesTheCall()
    {
        var channel = await AddVoiceChannelAsync(1, 2);
        var id = Guid.NewGuid();
        await Act(id, ChatCallAction.Start);
        channel.Verify(p => p.RemoveReplacedPeerAsync(1), Times.Once);
        channel.Verify(p => p.RemoveReplacedPeerAsync(2), Times.Never);
        await Act(id, ChatCallAction.Join, 2, "callee");
        channel.Verify(p => p.RemoveReplacedPeerAsync(2), Times.Once);
        await _manager.LeaveCallsAsync(2);
        Assert.False(_manager.IsInCall(2));
        Assert.True(_manager.IsInCall(1));
    }

    [Fact]
    public async Task JoiningChannelOnAnotherDevice_EndsTheCall()
    {
        var channel = await AddVoiceChannelAsync();
        var id = Guid.NewGuid();
        await Act(id, ChatCallAction.Start);
        _peers.Setup(p => p.HasPeer(1)).Returns(true);

        Assert.Null((await _manager.JoinPeerAsync(20, 1, "epoch", "caller-other")).result);

        _peers.Verify(p => p.RemoveReplacedPeerAsync(1), Times.Once);
        _peers.Verify(p => p.RemovePeerAsync(1), Times.Never);
        Assert.False(_manager.IsInCall(1));
        channel.Verify(p => p.AddPeerAsync(1, "epoch", "caller-other", false, false), Times.Once);
        Assert.Equal(ChatCallState.Ended, _sent.Last(s => s.Device == "caller").Call.State);
    }

    [Fact]
    public async Task ConcurrentChannelJoinAndCallStart_ReleaseTheChannelBeforeCalling()
    {
        var channel = await AddVoiceChannelAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.Setup(p => p.AddPeerAsync(1, "epoch", "caller", false, false))
            .Returns(async () =>
            {
                entered.SetResult();
                await release.Task;
                channel.Setup(p => p.HasPeer(1)).Returns(true);
                return ((ErrorCode?)null, (Shared.Models.Media.Transport.TransportData?)null);
            });
        var joining = _manager.JoinPeerAsync(20, 1, "epoch", "caller");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var calling = Act(Guid.NewGuid(), ChatCallAction.Start, device: "caller-other");
        Assert.False(calling.IsCompleted);
        release.SetResult();

        Assert.Null((await joining).result);
        Assert.Null((await calling).Error);
        channel.Verify(p => p.RemoveReplacedPeerAsync(1), Times.Once);
        Assert.True(_manager.IsInCall(1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureToReleaseCall_PreventsJoiningOtherMedia(bool joiningCall)
    {
        var channel = await AddVoiceChannelAsync();
        await Act(Guid.NewGuid(), ChatCallAction.Start);
        _peers.Setup(p => p.HasPeer(1)).Returns(true);
        _peers.Setup(p => p.RemovePeerAsync(1)).ReturnsAsync(ErrorCode.TimedOut);
        _peers.Setup(p => p.RemoveReplacedPeerAsync(1)).ReturnsAsync(ErrorCode.TimedOut);

        var error = joiningCall
            ? (await Act(Guid.NewGuid(), ChatCallAction.Start, device: "caller-other", chat: 8)).Error
            : (await _manager.JoinPeerAsync(20, 1, "epoch", "caller-other")).result;

        Assert.Equal(ErrorCode.TimedOut, error);
        Assert.True(_manager.IsInCall(1));
        channel.Verify(p => p.AddPeerAsync(1, It.IsAny<string>(), It.IsAny<string>(), false, false), Times.Never);
    }

    [Fact]
    public async Task MediaJoinAuthorizedBeforeCallTakeover_IsRejectedAfterwards()
    {
        var id = Guid.NewGuid();
        await Act(id, ChatCallAction.Start);
        await Act(id, ChatCallAction.Join, 1, "caller-other");

        Assert.Equal(ErrorCode.NotMember,
            (await _manager.JoinPeerAsync(7, 1, "old-epoch", "caller")).result);
        _peers.Verify(p => p.AddPeerAsync(It.IsAny<int>(), It.IsAny<string>(), "caller", false, false), Times.Never);
    }

    [Fact]
    public async Task DisconnectingTheSelectedDevice_LeavesTheCall()
    {
        var id = Guid.NewGuid();
        await Act(id, ChatCallAction.Start);
        await Act(id, ChatCallAction.Join, 2, "callee");
        await _manager.RemoveConnectionFromAllSessionsAsync(2, "callee-other");
        Assert.True(_manager.IsInCall(2));
        await _manager.RemoveConnectionFromAllSessionsAsync(2, "callee");
        Assert.False(_manager.IsInCall(2));
        await _manager.RemoveUserFromAllSessionsAsync(1);
        Assert.False(_manager.TryGetSession(7, out _));
        Assert.Equal(ChatCallEndReason.Completed, _history.Last().CallEndReason);
    }

    [Fact]
    public async Task ServerShutdown_EndsCallsAsDisconnected()
    {
        await Act(Guid.NewGuid(), ChatCallAction.Start);
        await _manager.CloseAllSessionsAsync();
        Assert.Equal(ChatCallEndReason.Disconnected, _history.Last().CallEndReason);
        Assert.False(_manager.IsInCall(1));
    }

    [Fact]
    public async Task LeavingThroughTheMediaControls_LeavesTheCall()
    {
        var id = Guid.NewGuid();
        await Act(id, ChatCallAction.Start);
        await Act(id, ChatCallAction.Join, 2, "callee");
        await _manager.LeaveCallForSessionAsync(7, 2, "callee-other");
        Assert.True(_manager.IsInCall(2));
        await _manager.LeaveCallForSessionAsync(7, 2, "callee");
        Assert.False(_manager.IsInCall(2));
        Assert.True(_manager.TryGetSession(7, out _));
    }

    [Fact]
    public async Task NoSfu_DoesNotAnnounceACallOrWriteAStart()
    {
        _sfuReady = false;
        Assert.Equal(ErrorCode.SFUUnavailable, (await Act(Guid.NewGuid(), ChatCallAction.Start)).Error);
        Assert.Empty(_history);
        Assert.Empty(_sent);
        Assert.False(_manager.TryGetSession(7, out _));
    }

    [Fact]
    public async Task ReconnectingMember_GetsTheRunningCallsOfTheirChats()
    {
        await Act(Guid.NewGuid(), ChatCallAction.Start, chat: 8);
        _sent.Clear();
        await _manager.PublishCurrentCallAsync(3, "outsider");
        await _manager.PublishCurrentCallAsync(3, "stranger-device");
        var sent = Assert.Single(_sent);
        Assert.Equal("outsider", sent.Device);
        Assert.Equal(8, sent.Call.ChatId);
    }

    [Fact]
    public async Task MembersInTheCall_MayShareVideo_ButStrangersNothing()
    {
        var id = Guid.NewGuid();
        await Act(id, ChatCallAction.Start);
        var authorizer = new Server.Application.Coordinator.Roles.RequestAuthorizer(
            Mock.Of<Server.Application.Coordinator.Roles.ICoordinatorPermissions>(), _manager,
            Mock.Of<Server.Application.Coordinator.Sessions.ISessionManager>());
        Assert.True(await authorizer.IsAllowedAsync(new Shared.Models.Requests.SessionActions.HybridSession.HybridSessionStartVoiceRequest(7) { SenderId = 1 }));
        Assert.True(await authorizer.IsAllowedAsync(new Shared.Models.Requests.SessionActions.HybridSession.HybridSessionStartVideoRequest(7) { SenderId = 1 }));
        Assert.True(await authorizer.IsAllowedAsync(new Shared.Models.Requests.SessionActions.HybridSession.HybridSessionStartScreenShareRequest(7, false) { SenderId = 1 }));
        // Rung but not joined yet
        Assert.False(await authorizer.IsAllowedAsync(new Shared.Models.Requests.SessionActions.HybridSession.HybridSessionStartVoiceRequest(7) { SenderId = 2 }));
        Assert.False(await authorizer.IsAllowedAsync(new Shared.Models.Requests.SessionActions.HybridSession.HybridSessionStartVoiceRequest(7) { SenderId = 3 }));
        Assert.True(await authorizer.IsAllowedAsync(new Shared.Models.Requests.SessionActions.Generic.KickFromSessionRequest(7, 2, null) { SenderId = 1 }));
    }

    [Fact]
    public async Task UnselectedDeviceCannotJoinOrReadTheMediaSession()
    {
        var id = Guid.NewGuid();
        await Act(id, ChatCallAction.Start);
        await Act(id, ChatCallAction.Join, 2, "callee");
        var replies = new List<BaseMessage>();
        var sender = new Mock<IMessageSender>();
        sender.Setup(s => s.SendMessageById(It.IsAny<string>(), It.IsAny<BaseMessage>()))
            .Callback((string _, BaseMessage message) => replies.Add(message));
        var join = new Server.Application.Coordinator.Handlers.Hybrid.HybridSessionJoinHandler(_manager, sender.Object);
        await join.HandleMessage(new Shared.Models.Requests.SessionActions.HybridSession.HybridSessionJoinRequest(7)
            { SenderId = 1 }, "caller-other");
        var peers = new Server.Application.Coordinator.Handlers.Hybrid.HybridSessionPeerListRequestHandler(_manager, sender.Object);
        await peers.HandleMessage(new Shared.Models.Requests.SessionActions.HybridSession.HybridSessionPeerListRequest(7)
            { SenderId = 3 }, "outsider");
        Assert.All(replies, response => Assert.Equal(ErrorCode.NotMember,
            Assert.IsType<Shared.Models.Responses.ErrorResponse>(response).Code));
        Assert.Equal(2, replies.Count);
    }

    [Fact]
    public async Task MembersOfTheChat_ReadThePeersOfItsCall_WithoutBeingInIt()
    {
        await Act(Guid.NewGuid(), ChatCallAction.Start, chat: 8);
        var replies = new List<BaseMessage>();
        var sender = new Mock<IMessageSender>();
        sender.Setup(s => s.SendMessageById(It.IsAny<string>(), It.IsAny<BaseMessage>()))
            .Callback((string _, BaseMessage message) => replies.Add(message));
        var sessions = new Mock<Server.Application.Coordinator.Sessions.ISessionManager>();
        Server.Core.Models.SessionModel? group = _group;
        sessions.Setup(s => s.TryGetSessionModel(8, out group)).Returns(true);
        var peers = new Server.Application.Coordinator.Handlers.Hybrid.HybridSessionPeerListRequestHandler(
            _manager, sender.Object, sessions.Object);

        // A member who logged in while the call runs, and the caller on a device that is not the call's
        await peers.HandleMessage(new Shared.Models.Requests.SessionActions.HybridSession.HybridSessionPeerListRequest(8)
            { SenderId = 3 }, "outsider");
        await peers.HandleMessage(new Shared.Models.Requests.SessionActions.HybridSession.HybridSessionPeerListRequest(8)
            { SenderId = 1 }, "caller-other");

        Assert.Equal(2, replies.Count);
        Assert.All(replies, response =>
            Assert.IsType<Shared.Models.Responses.SessionActions.HybridSession.HybridSessionPeerListResponse>(response));
    }

    [Fact]
    public async Task MediaCleanupFailureStillRecordsAndAnnouncesTheOutcome()
    {
        var id = Guid.NewGuid();
        await Act(id, ChatCallAction.Start);
        _session.Setup(s => s.CloseSession()).ThrowsAsync(new InvalidOperationException("SFU disconnected"));
        Assert.Null((await Act(id, ChatCallAction.Leave)).Error);
        Assert.False(_manager.TryGetSession(7, out _));
        Assert.Equal(ChatSystemEventKind.CallEnded, _history.Last().Kind);
        Assert.Equal(ChatCallState.Ended, _sent.Last().Call.State);
    }

    public void Dispose() => _manager.Dispose();
}
