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
using Server.Application.Tests.Fakes;
using Server.Core.Models.HybridSession;
using Shared.Enums;
using Shared.Enums.WebRTC;

namespace Server.Application.Tests.Coordinator;

public class HybridSessionMovePeerTests : IDisposable
{
    private const int ModeratorId = 1;
    private const int UserId = 2;
    private const int OtherUserId = 3;
    private const int RoomId = 7;
    private const int HallId = 8;
    private const int CallId = 9;

    private readonly RecordingEventBus _events = new();
    private readonly FakeSfuGateway _gateway = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly Dictionary<int, HybridSessionPeerManager> _peers = new();
    private readonly HybridSessionModel _room = new(RoomId, "coordinator", "room", 10, WebRTCEngine.Mediasoup);
    // Full and asking for approval: a moved peer gets in all the same
    private readonly HybridSessionModel _hall = new(HallId, "coordinator", "hall", 2, WebRTCEngine.Mediasoup,
        isApprovalRequired: true);
    private readonly HybridSessionModel _call = new(CallId, "coordinator", "call", 2, WebRTCEngine.Mediasoup)
    {
        CallDevices = new Dictionary<int, string> { [UserId] = "device", [OtherUserId] = "other" }
    };
    private readonly HybridSessionManager _manager;

    public HybridSessionMovePeerTests()
    {
        var repository = new Mock<IVideoSessionRepository>();
        repository.Setup(r => r.GetByCoordinatorIdAsync("coordinator", It.IsAny<CancellationToken>()))
            .ReturnsAsync([_room, _hall, _call]);

        var factory = new Mock<IVideoSessionFactory>();
        factory.Setup(f => f.CreateHybridSession(It.IsAny<HybridSessionModel>(), It.IsAny<ICoordinatorEventBus>(),
                It.IsAny<ISFUNodeSelector>()))
            .Returns((HybridSessionModel model, ICoordinatorEventBus _, ISFUNodeSelector _) =>
            {
                var peers = new HybridSessionPeerManager(NullLogger.Instance, _events, model);
                peers.ChangeGatewayAsync(_gateway).GetAwaiter().GetResult();
                _peers[model.Id] = peers;

                var session = new Mock<IHybridSession>();
                session.Setup(s => s.Model).Returns(model);
                session.Setup(s => s.PeerManager).Returns(peers);
                return session.Object;
            });

        _manager = new HybridSessionManager(NullLogger.Instance, _events, factory.Object,
            Mock.Of<ISFUNodeSelector>(), repository.Object, "coordinator");
        _manager.ConfigureCalls(Mock.Of<IChatSessionManager>(), Mock.Of<IUserManager>(), Mock.Of<IMessageSender>(),
            Mock.Of<IConnectionsContext>(), _time);
        _manager.LoadAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _manager.Dispose();

    private async Task JoinAsync(int sessionId, int userId, string connectionId = "device")
    {
        Assert.Null((await _peers[sessionId].AddPeerAsync(userId, "epoch", connectionId)).result);
        Assert.Null(await _peers[sessionId].ConnectPeerAsync(userId, FakeSfuGateway.Dtls()));
    }

    private Task<ErrorCode?> MoveAsync(int sessionId = RoomId, int userId = UserId, int targetId = HallId) =>
        _manager.MovePeerAsync(ModeratorId, sessionId, userId, targetId);

    [Fact]
    public async Task Move_TakesThePeerOut_AndKeepsAPlaceInTheTarget_PastCapacityAndApproval()
    {
        await JoinAsync(HallId, ModeratorId);
        await JoinAsync(HallId, OtherUserId);
        await JoinAsync(RoomId, UserId);

        Assert.Null(await MoveAsync());

        Assert.False(_peers[RoomId].HasPeer(UserId));
        var moved = _peers[HallId].GetAllPeerSnapshots()[UserId];
        Assert.Equal(HybridSessionPeerState.WaitingForServer, moved.State);
        Assert.True(_peers[HallId].TryGetPeerConnection(UserId, out var connection));
        Assert.Equal("device", connection);

        var movedOut = Assert.Single(_events.Of<HybridSessionPeerMovedOutEvent>());
        Assert.Equal((RoomId, UserId, HallId, ModeratorId),
            (movedOut.SessionId, movedOut.UserId, movedOut.TargetSessionId, movedOut.InitiatorUserId));
        var movedIn = Assert.Single(_events.Of<HybridSessionPeerMovedInEvent>());
        Assert.Equal((HallId, UserId, RoomId), (movedIn.SessionId, movedIn.Peer.UserId, movedIn.SourceSessionId));
        Assert.Empty(_events.Of<HybridSessionPeerLeftEvent>());
    }

    [Fact]
    public async Task MovedPeer_JoinsTheTargetAsUsual_AndKeepsItsPlace()
    {
        await JoinAsync(RoomId, UserId);
        Assert.Null(await MoveAsync());

        await JoinAsync(HallId, UserId);

        Assert.Equal(HybridSessionPeerState.Connected, _peers[HallId].GetAllPeerSnapshots()[UserId].State);
        _time.Advance(HybridSessionManager.MovedPeerJoinTimeout);
        await Task.Delay(50);
        Assert.True(_peers[HallId].HasPeer(UserId));
    }

    [Fact]
    public async Task MovedPeer_ThatDoesNotJoinInTime_LosesItsPlace_WithoutTouchingTheSfu()
    {
        await JoinAsync(RoomId, UserId);
        Assert.Null(await MoveAsync());
        var transportsClosed = _gateway.CallCount("ClosePeerTransport");

        _time.Advance(HybridSessionManager.MovedPeerJoinTimeout);

        for (var i = 0; i < 100 && _peers[HallId].HasPeer(UserId); i++)
            await Task.Delay(10);
        Assert.False(_peers[HallId].HasPeer(UserId));
        Assert.Contains(_events.Of<HybridSessionPeerLeftEvent>(), e => e.SessionId == HallId && e.UserId == UserId);
        Assert.Equal(transportsClosed, _gateway.CallCount("ClosePeerTransport"));
    }

    [Fact]
    public async Task PeerWaitingForApproval_CanBeMoved()
    {
        _room.SetApprovalRequired(true);
        Assert.Null((await _peers[RoomId].AddPeerAsync(UserId, "epoch", "device")).result);
        Assert.Equal(HybridSessionPeerState.WaitingForApprove, _peers[RoomId].GetAllPeerSnapshots()[UserId].State);

        Assert.Null(await MoveAsync());

        Assert.False(_peers[RoomId].HasPeer(UserId));
        Assert.True(_peers[HallId].HasPeer(UserId));
    }

    [Fact]
    public async Task UserInNoSession_CannotBeMoved()
    {
        Assert.Equal(ErrorCode.PeerNotFound, await MoveAsync());

        Assert.False(_peers[HallId].HasPeer(UserId));
        Assert.Empty(_events.Of<HybridSessionPeerMovedInEvent>());
    }

    [Fact]
    public async Task UserAlreadyInTheTarget_IsRefused_AndStays()
    {
        await JoinAsync(RoomId, UserId);
        await JoinAsync(HallId, UserId, "other");

        Assert.Equal(ErrorCode.Conflict, await MoveAsync());

        Assert.True(_peers[RoomId].HasPeer(UserId));
        Assert.True(_peers[HallId].GetAllPeerSnapshots()[UserId].IsTransportConnected);
        Assert.Empty(_events.Of<HybridSessionPeerMovedInEvent>());
    }

    [Theory]
    [InlineData(RoomId, CallId, ErrorCode.WrongSessionType)]
    [InlineData(CallId, RoomId, ErrorCode.WrongSessionType)]
    [InlineData(RoomId, RoomId, ErrorCode.InvalidValue)]
    [InlineData(RoomId, 42, ErrorCode.SessionNotFound)]
    public async Task Move_BetweenWrongSessions_IsRefused(int sessionId, int targetId, ErrorCode expected)
    {
        await JoinAsync(RoomId, UserId);

        Assert.Equal(expected, await MoveAsync(sessionId, targetId: targetId));

        Assert.True(_peers[RoomId].HasPeer(UserId));
    }

    [Fact]
    public async Task MovingOneself_IsRefused()
    {
        await JoinAsync(RoomId, ModeratorId);

        Assert.Equal(ErrorCode.CannotTargetSelf, await MoveAsync(userId: ModeratorId));
    }

    [Fact]
    public async Task ConnectionLost_RemovesTheKeptPlace()
    {
        await JoinAsync(RoomId, UserId);
        Assert.Null(await MoveAsync());

        await _manager.RemoveConnectionFromAllSessionsAsync(UserId, "device");

        Assert.False(_peers[HallId].HasPeer(UserId));
    }

    [Fact]
    public async Task MoveOut_OfAPeerThatSwitchedDevice_IsRefused_AndGivesUpTheKeptPlace()
    {
        await JoinAsync(RoomId, UserId);
        Assert.Null(await _peers[HallId].MoveInPeerAsync(ModeratorId, UserId, "device", RoomId));
        await JoinAsync(RoomId, UserId, "phone");

        Assert.Equal(ErrorCode.PeerNotFound,
            await _peers[RoomId].MoveOutPeerAsync(ModeratorId, UserId, "device", HallId));
        Assert.Null(await _peers[HallId].RemoveUnclaimedPeerAsync(UserId));

        Assert.True(_peers[RoomId].HasPeer(UserId));
        Assert.False(_peers[HallId].HasPeer(UserId));
    }

    [Fact]
    public async Task RemoveUnclaimedPeer_KeepsAPeerThatJoined()
    {
        await JoinAsync(HallId, UserId);

        Assert.Equal(ErrorCode.PeerNotFound, await _peers[HallId].RemoveUnclaimedPeerAsync(UserId));
        Assert.True(_peers[HallId].HasPeer(UserId));
    }
}
