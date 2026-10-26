using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Core.Enums;
using Server.Core.Models.Coordinator;
using Server.Core.Models;
using Server.Core.Models.HybridSession;
using Server.Core.Models.User;
using Shared.Enums;
using Server.Application.Coordinator;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Users;
using Server.Application.Coordinator.Heartbeat;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Messaging;

namespace Server.Application.Tests.Coordinator;

public class CoordinatorInstanceTests
{
    private readonly Mock<ISessionManager> _sessions = new();
    private readonly Mock<IHybridSessionManager> _hybridSessions = new();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<IGatewayService> _gateway = new();
    private readonly CoordinatorInstance _coordinator;

    public CoordinatorInstanceTests()
    {
        _users.Setup(u => u.GetAllConnectedUsers()).Returns(new Dictionary<int, UserModel>());
        _hybridSessions.Setup(h => h.GetAllSessions()).Returns([]);

        var model = CoordinatorInstanceModel.Create("coordinator", "127.0.0.1", 5000, DateTime.UtcNow);

        _coordinator = new CoordinatorInstance(
            new CoordinatorInstanceContext(model, "1.0"),
            NullLogger.Instance,
            _sessions.Object,
            _hybridSessions.Object,
            _users.Object,
            new Mock<IMessageSender>().Object,
            new Mock<IMessageRouter>().Object,
            _gateway.Object,
            new Mock<IConnectionManager>().Object,
            new Mock<IHeartbeatManager>().Object);
    }

    [Fact]
    public async Task Start_LoadsSessionsOpensTheGatewayAndGoesOnline()
    {
        await _coordinator.StartAsync();

        Assert.Equal(CoordinatorState.Online, _coordinator.State);
        _sessions.Verify(s => s.LoadSessionsAsync(), Times.Once);
        _gateway.Verify(g => g.StartAsync(It.IsAny<IMessageRouter>()), Times.Once);
    }

    [Fact]
    public async Task Start_Twice_StartsOnce()
    {
        await _coordinator.StartAsync();
        await _coordinator.StartAsync();

        _sessions.Verify(s => s.LoadSessionsAsync(), Times.Once);
    }

    [Fact]
    public async Task Start_WhenLoadingFails_ClosesAndEndsFailed()
    {
        _sessions.Setup(s => s.LoadSessionsAsync()).ThrowsAsync(new InvalidOperationException());

        await Assert.ThrowsAsync<InvalidOperationException>(_coordinator.StartAsync);

        Assert.Equal(CoordinatorState.Failed, _coordinator.State);
        _gateway.Verify(g => g.StopAsync(), Times.Once);
    }

    [Fact]
    public async Task Close_ClosesSessionsAndGatewayOnce()
    {
        await _coordinator.StartAsync();

        await _coordinator.CloseAsync();
        await _coordinator.CloseAsync();

        Assert.Equal(CoordinatorState.Closed, _coordinator.State);
        _sessions.Verify(s => s.CloseAllSessionsAsync(), Times.Once);
        _gateway.Verify(g => g.StopAsync(), Times.Once);
    }

    [Fact]
    public void Snapshot_CopiesSessionsPeersAndConnectedUsers()
    {
        var now = DateTime.UtcNow;
        var user = UserModel.Create("coordinator", "alice", "10.0.0.1", now);
        user.Id = 7;
        _users.Setup(u => u.GetAllConnectedUsers()).Returns(new Dictionary<int, UserModel> { [7] = user });

        var peer = new PeerSnapshot(7, HybridSessionPeerState.Connected, true, true, false, false);
        _hybridSessions.Setup(h => h.GetAllSessions()).Returns([
            CreateSession(2, "empty"),
            CreateSession(1, "talk", peer)
        ]);

        var snapshot = _coordinator.GetSnapshot();

        Assert.Equal(CoordinatorState.Offline, snapshot.State);
        Assert.Equal([1, 2], snapshot.HybridSessions.Select(s => s.Id));
        Assert.Equal(peer, Assert.Single(snapshot.HybridSessions[0].Peers));
        Assert.Empty(snapshot.HybridSessions[1].Peers);
        var connected = Assert.Single(snapshot.ConnectedUsers);
        Assert.Equal((7, "alice"), (connected.Id, connected.Username));
        Assert.Equal(1, snapshot.ActiveSessionsCount);
    }

    [Fact]
    public void ActiveSessionsCount_CountsOnlySessionsWithPeers()
    {
        var peer = new PeerSnapshot(1, HybridSessionPeerState.Connected, true, true, false, false);
        _hybridSessions.Setup(h => h.GetAllSessions()).Returns([
            CreateSession(1, "a", peer),
            CreateSession(2, "b"),
            CreateSession(3, "c", peer)
        ]);

        Assert.Equal(2, _coordinator.ActiveSessionsCount);
    }

    private static IHybridSession CreateSession(int id, string name, params PeerSnapshot[] peers)
    {
        var peerManager = new Mock<IHybridSessionPeerManager>();
        peerManager.Setup(p => p.GetAllPeerSnapshots()).Returns(peers.ToDictionary(peer => peer.UserId));

        var session = new Mock<IHybridSession>();
        session.Setup(s => s.Model).Returns(
            new HybridSessionModel(id, "coordinator", name, SessionModel.NoCapacityLimit, 1, default));
        session.Setup(s => s.PeerManager).Returns(peerManager.Object);
        return session.Object;
    }
}
