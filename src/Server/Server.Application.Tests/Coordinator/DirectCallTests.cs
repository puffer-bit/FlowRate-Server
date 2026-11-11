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

public sealed class DirectCallTests : IDisposable
{
    private readonly Mock<IChatSessionManager> _chats = new();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<IVideoSessionFactory> _factory = new();
    private readonly Mock<IHybridSession> _session = new();
    private readonly Mock<IHybridSessionPeerManager> _peers = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ConcurrentQueue<(string Device, DirectCallDTO Call)> _sent = new();
    private readonly ConcurrentQueue<ChatSystemContent> _history = new();
    private readonly HybridSessionManager _manager;
    private readonly Mock<IVideoSessionRepository> _repository = new();
    private readonly TaskCompletionSource _expired = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _sfuReady = true;
    private readonly ConcurrentDictionary<int, string[]> _devices = new();

    public DirectCallTests()
    {
        var chat = ChatSessionModel.Create("server", "Direct", 1);
        chat.Id = 7;
        chat.SetAudience(ChatKind.Direct, new[] { 1, 2 });
        _chats.Setup(c => c.TryGetSession(7, out chat)).Returns(true);
        _chats.Setup(c => c.AddCallSystemMessageAsync(7, It.IsAny<ChatSystemContent>()))
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
        var sender = new Mock<IMessageSender>();
        sender.Setup(s => s.SendMessageById(It.IsAny<string>(), It.IsAny<BaseMessage>()))
            .Callback((string device, BaseMessage message) =>
            {
                if (message is not DirectCallChangedNotification changed) return;
                _sent.Enqueue((device, changed.Call));
                if (changed.Call.EndReason == DirectCallEndReason.Missed) _expired.TrySetResult();
            });
        _manager = new HybridSessionManager(NullLogger.Instance, Mock.Of<ICoordinatorEventBus>(), _factory.Object,
            Mock.Of<ISFUNodeSelector>(), _repository.Object, "server");
        _manager.ConfigureCalls(_chats.Object, _users.Object, sender.Object, connections.Object, _time);
    }

    private Task<(ErrorCode? Error, DirectCallDTO? Call)> Act(Guid id, DirectCallAction action,
        int user = 1, string device = "caller") =>
        _manager.ChangeCallAsync(new DirectCallRequest(7, id, action) { SenderId = user }, device);

    private async Task<Mock<IHybridSessionPeerManager>> AddVoiceChannelAsync(params int[] users)
    {
        var members = users.ToHashSet();
        var peers = new Mock<IHybridSessionPeerManager>();
        peers.Setup(p => p.HasPeer(It.IsAny<int>())).Returns((int user) => members.Contains(user));
        peers.Setup(p => p.RemovePeerAsync(It.IsAny<int>()))
            .Callback((int user) => members.Remove(user)).ReturnsAsync((ErrorCode?)null);
        var model = HybridSessionModel.Create("server", "Voice", 10, 1, Shared.Enums.WebRTC.WebRTCEngine.Mediasoup);
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
    public async Task StartingCall_LeavesCallerChannel_AndKeepsRecipientUntilAcceptance()
    {
        var peers = await AddVoiceChannelAsync(1, 2, 3);
        var id = Guid.NewGuid();
        var ringing = await Act(id, DirectCallAction.Start);
        Assert.Null(ringing.Error);
        Assert.False(peers.Object.HasPeer(1));
        Assert.True(peers.Object.HasPeer(2));
        Assert.True(peers.Object.HasPeer(3));
        var accepted = await Act(id, DirectCallAction.Accept, 2, "callee");
        Assert.Null(accepted.Error);
        Assert.Equal(DirectCallState.Active, accepted.Call!.State);
        Assert.False(peers.Object.HasPeer(2));
        Assert.True(peers.Object.HasPeer(3));
        peers.Verify(p => p.RemovePeerAsync(1), Times.Once);
        peers.Verify(p => p.RemovePeerAsync(2), Times.Once);
    }

    [Theory]
    [InlineData(DirectCallAction.Decline, 2, "callee")]
    [InlineData(DirectCallAction.End, 1, "caller")]
    public async Task UnansweredCall_DoesNotRemoveRecipientFromChannel(DirectCallAction action, int user, string device)
    {
        var peers = await AddVoiceChannelAsync(2);
        var id = Guid.NewGuid();
        Assert.Null((await Act(id, DirectCallAction.Start)).Error);
        Assert.Null((await Act(id, action, user, device)).Error);
        Assert.True(peers.Object.HasPeer(2));
        peers.Verify(p => p.RemovePeerAsync(2), Times.Never);
    }

    [Fact]
    public async Task FailedMediaCreation_KeepsRecipientInChannel()
    {
        var peers = await AddVoiceChannelAsync(2);
        _sfuReady = false;
        var id = Guid.NewGuid();
        await Act(id, DirectCallAction.Start);
        Assert.Equal(ErrorCode.SFUUnavailable, (await Act(id, DirectCallAction.Accept, 2, "callee")).Error);
        Assert.True(peers.Object.HasPeer(2));
    }

    [Fact]
    public async Task OfflineRecipient_CanReconnectAndAcceptPendingInvitation()
    {
        UserModel? offline = null;
        _users.Setup(u => u.TryGetConnectedUser(2, out offline)).Returns(false);
        _devices.TryRemove(2, out _);
        var id = Guid.NewGuid();
        var started = await Act(id, DirectCallAction.Start);
        Assert.Null(started.Error);
        Assert.Equal(DirectCallState.Ringing, started.Call!.State);
        Assert.DoesNotContain(_sent, message => message.Device == "callee");
        await _manager.RemoveUserFromAllSessionsAsync(2);
        Assert.True(_manager.HasDirectCall(2));
        _devices[2] = ["new-phone"];
        await _manager.PublishCurrentCallAsync(2, "new-phone");
        Assert.Equal(id, _sent.Last(message => message.Device == "new-phone").Call.CallId);
        Assert.Equal(DirectCallState.Active, (await Act(id, DirectCallAction.Accept, 2, "new-phone")).Call!.State);
    }

    [Fact]
    public async Task CurrentInvitation_IsSentOnlyToAuthenticatedRecipientDevices()
    {
        await Act(Guid.NewGuid(), DirectCallAction.Start);
        var count = _sent.Count;
        await _manager.PublishCurrentCallAsync(2, "outsider");
        await _manager.PublishCurrentCallAsync(3, "outsider");
        Assert.Equal(count, _sent.Count);
    }

    [Fact]
    public async Task Ringing_IsPrivate_AndDoesNotCreateMedia()
    {
        var result = await Act(Guid.NewGuid(), DirectCallAction.Start);
        Assert.Null(result.Error);
        Assert.Equal(DirectCallState.Ringing, result.Call!.State);
        Assert.Equal(new[] { "callee", "callee-other", "caller", "caller-other" }, _sent.Select(n => n.Device).Order());
        Assert.True(_sent.Single(n => n.Device == "caller").Call.IsSelectedDevice);
        Assert.False(_sent.Single(n => n.Device == "caller-other").Call.IsSelectedDevice);
        Assert.All(_sent, n => Assert.Null(n.Call.Session));
        _factory.Verify(f => f.CreateHybridSession(It.IsAny<HybridSessionModel>(), It.IsAny<ICoordinatorEventBus>(),
            It.IsAny<ISFUNodeSelector>()), Times.Never);
    }

    [Fact]
    public async Task OutsiderAndUnselectedCallerCannotDriveCall()
    {
        var id = Guid.NewGuid();
        Assert.Equal(ErrorCode.NotMember, (await Act(id, DirectCallAction.Start, 3, "outsider")).Error);
        await Act(id, DirectCallAction.Start);
        Assert.Equal(ErrorCode.NotMember, (await Act(id, DirectCallAction.End, 1, "caller-other")).Error);
        Assert.Equal(ErrorCode.NotConnected, (await Act(id, DirectCallAction.Accept, 2, "caller")).Error);
        Assert.Equal(ErrorCode.NotMember, (await Act(id, DirectCallAction.Accept)).Error);
    }

    [Fact]
    public async Task Accept_SelectsOneDevice_AndKeepsMediaOutOfChannelList()
    {
        var id = Guid.NewGuid();
        await Act(id, DirectCallAction.Start);
        var result = await Act(id, DirectCallAction.Accept, 2, "callee");
        Assert.Null(result.Error);
        Assert.Equal(DirectCallState.Active, result.Call!.State);
        Assert.NotNull(result.Call.Session);
        Assert.Empty(_manager.GetAllSessions());
        Assert.True(_manager.TryGetSession(7, out var session));
        Assert.Equal("caller", session.Model.CallDevices![1]);
        Assert.Equal("callee", session.Model.CallDevices[2]);
        Assert.Null(_sent.Last(n => n.Device == "callee-other").Call.Session);
        Assert.Equal(ErrorCode.Conflict, (await Act(id, DirectCallAction.Accept, 2, "callee-other")).Error);
        await Act(id, DirectCallAction.Accept, 2, "callee");
        Assert.Single(_history, h => h.Kind == ChatSystemEventKind.CallStarted);
    }

    [Fact]
    public async Task RepeatedStartAndEnd_AreHarmless_AndDurationIsStoredOnce()
    {
        var id = Guid.NewGuid();
        await Act(id, DirectCallAction.Start);
        await Act(id, DirectCallAction.Start);
        await Act(id, DirectCallAction.Accept, 2, "callee");
        _time.Advance(TimeSpan.FromSeconds(67));
        await Act(id, DirectCallAction.End);
        await Act(id, DirectCallAction.End);
        Assert.False(_manager.TryGetSession(7, out _));
        var ended = Assert.Single(_history, h => h.Kind == ChatSystemEventKind.CallEnded);
        Assert.Equal(67, ended.CallDurationSeconds);
        Assert.Equal(DirectCallEndReason.Completed, ended.CallEndReason);
        _session.Verify(s => s.CloseSession(), Times.Once);
    }

    [Theory]
    [InlineData(DirectCallAction.Decline, 2, "callee", DirectCallEndReason.Declined)]
    [InlineData(DirectCallAction.End, 1, "caller", DirectCallEndReason.Cancelled)]
    public async Task UnansweredCall_StoresItsOutcome(DirectCallAction action, int user, string device,
        DirectCallEndReason reason)
    {
        var id = Guid.NewGuid();
        await Act(id, DirectCallAction.Start);
        var result = await Act(id, action, user, device);
        Assert.Equal(DirectCallState.Ended, result.Call!.State);
        Assert.Equal(reason, result.Call.EndReason);
        var ended = Assert.Single(_history);
        Assert.Equal(reason, ended.CallEndReason);
        Assert.Null(ended.CallDurationSeconds);
        Assert.True(ended.IsValid());
    }

    [Fact]
    public async Task Timeout_ClosesAllInvitations_AndReleasesBusyState()
    {
        var id = Guid.NewGuid();
        await Act(id, DirectCallAction.Start);
        _time.Advance(TimeSpan.FromSeconds(45));
        await _expired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(DirectCallEndReason.Missed, (await Act(id, DirectCallAction.Accept, 2, "callee")).Call!.EndReason);
        Assert.Null((await Act(Guid.NewGuid(), DirectCallAction.Start)).Error);
    }

    [Fact]
    public async Task CompetingStarts_ReserveBothUsersOnce()
    {
        var results = await Task.WhenAll(Act(Guid.NewGuid(), DirectCallAction.Start),
            Act(Guid.NewGuid(), DirectCallAction.Start, 2, "callee"));
        Assert.Single(results, r => r.Error == null);
        Assert.Single(results, r => r.Error == ErrorCode.Conflict);
    }

    [Fact]
    public async Task DisconnectingUnselectedDevice_DoesNotEndCall()
    {
        var id = Guid.NewGuid();
        await Act(id, DirectCallAction.Start);
        await Act(id, DirectCallAction.Accept, 2, "callee");
        await _manager.RemoveConnectionFromAllSessionsAsync(2, "callee-other");
        Assert.True(_manager.TryGetSession(7, out _));
        await _manager.RemoveConnectionFromAllSessionsAsync(1, "caller");
        Assert.False(_manager.TryGetSession(7, out _));
        Assert.Equal(DirectCallEndReason.Disconnected, _history.Last().CallEndReason);
    }

    [Fact]
    public async Task LeavingThroughExistingMediaControls_EndsBothSides()
    {
        var id = Guid.NewGuid();
        await Act(id, DirectCallAction.Start);
        await Act(id, DirectCallAction.Accept, 2, "callee");
        await _manager.EndCallForSessionAsync(7, 2, "callee");
        Assert.False(_manager.TryGetSession(7, out _));
        Assert.All(_sent.TakeLast(4), item => Assert.Equal(DirectCallState.Ended, item.Call.State));
    }

    [Fact]
    public async Task NoSfu_DoesNotAnnounceAnActiveCallOrWriteAStart()
    {
        _sfuReady = false;
        var id = Guid.NewGuid();
        await Act(id, DirectCallAction.Start);
        Assert.Equal(ErrorCode.SFUUnavailable, (await Act(id, DirectCallAction.Accept, 2, "callee")).Error);
        Assert.Empty(_history);
        Assert.False(_manager.TryGetSession(7, out _));
        Assert.DoesNotContain(_sent, item => item.Call.State == DirectCallState.Active);
    }

    [Fact]
    public async Task MediaAccessIgnoresServerRolesButRequiresCallMembership()
    {
        var id = Guid.NewGuid();
        await Act(id, DirectCallAction.Start);
        await Act(id, DirectCallAction.Accept, 2, "callee");
        var authorizer = new Server.Application.Coordinator.Roles.RequestAuthorizer(
            Mock.Of<Server.Application.Coordinator.Roles.ICoordinatorPermissions>(), _manager,
            Mock.Of<Server.Application.Coordinator.Sessions.ISessionManager>());
        Assert.True(await authorizer.IsAllowedAsync(new Shared.Models.Requests.SessionActions.HybridSession.HybridSessionStartVoiceRequest(7) { SenderId = 1 }));
        Assert.False(await authorizer.IsAllowedAsync(new Shared.Models.Requests.SessionActions.HybridSession.HybridSessionStartVoiceRequest(7) { SenderId = 3 }));
        Assert.False(await authorizer.IsAllowedAsync(new Shared.Models.Requests.SessionActions.HybridSession.HybridSessionStartVideoRequest(7) { SenderId = 1 }));
        Assert.False(await authorizer.IsAllowedAsync(new Shared.Models.Requests.SessionActions.HybridSession.HybridSessionStartScreenShareRequest(7, false) { SenderId = 1 }));
    }

    [Fact]
    public async Task UnselectedDeviceCannotJoinOrReadTheMediaSession()
    {
        var id = Guid.NewGuid();
        await Act(id, DirectCallAction.Start);
        await Act(id, DirectCallAction.Accept, 2, "callee");
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
    public async Task MediaCleanupFailureStillRecordsAndAnnouncesTheOutcome()
    {
        var id = Guid.NewGuid();
        await Act(id, DirectCallAction.Start);
        await Act(id, DirectCallAction.Accept, 2, "callee");
        _session.Setup(s => s.CloseSession()).ThrowsAsync(new InvalidOperationException("SFU disconnected"));
        Assert.Null((await Act(id, DirectCallAction.End)).Error);
        Assert.False(_manager.TryGetSession(7, out _));
        Assert.Equal(ChatSystemEventKind.CallEnded, _history.Last().Kind);
        Assert.All(_sent.TakeLast(4), notification => Assert.Equal(DirectCallState.Ended, notification.Call.State));
    }

    public void Dispose() => _manager.Dispose();
}
