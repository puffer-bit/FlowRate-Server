using Microsoft.AspNetCore.Mvc;
using Moq;
using Server.Application.Coordinator;
using Server.Application.Coordinator.Snapshots;
using Server.Application.Orchestrator;
using Server.Asp.Web.Api;
using Server.Core.Enums;
using Server.Core.Models;
using Server.Core.Models.HybridSession;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Enums.WebRTC;

namespace Server.Asp.Tests.Web.Api;

public class PanelControllerTests
{
    private static readonly DateTime LaunchedAt = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IOrchestratorInstance> _orchestrator = new();
    private readonly PanelController _controller;

    public PanelControllerTests()
    {
        var context = new Mock<IOrchestratorInstanceContext>();
        context.Setup(c => c.ServerVersion).Returns("1.2.3");
        context.Setup(c => c.CurrentLaunchTime).Returns(LaunchedAt);
        _orchestrator.Setup(o => o.Context).Returns(context.Object);

        _controller = new PanelController(_orchestrator.Object);
    }

    [Fact]
    public void Overview_SumsTheCoordinators()
    {
        var peer = Peer(1);
        _orchestrator.Setup(o => o.GetCoordinatorSnapshots()).Returns([
            Snapshot("a", sessions: [Session(1, peers: [peer]), Session(2)], users: [User(1), User(2)]),
            Snapshot("b", sessions: [Session(3, peers: [peer])], users: [User(3)])
        ]);

        var overview = _controller.GetOverview();

        Assert.Equal("1.2.3", overview.ServerVersion);
        Assert.Equal(LaunchedAt, overview.StartedAtUtc);
        Assert.Equal(2, overview.ActiveSessionsCount);
        Assert.Equal(3, overview.ConnectedUsersCount);
        Assert.Equal(["a", "b"], overview.Coordinators.Select(c => c.Id));
        var first = overview.Coordinators[0];
        Assert.Equal((2, 1, 2), (first.SessionsCount, first.ActiveSessionsCount, first.ConnectedUsersCount));
    }

    [Fact]
    public void Coordinator_Unknown_IsNotFound()
    {
        var result = _controller.GetCoordinator("missing");

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void Coordinator_Known_ReturnsSessionsPeersAndUsers()
    {
        var coordinator = new Mock<ICoordinatorInstance>();
        coordinator.Setup(c => c.GetSnapshot()).Returns(
            Snapshot("a", sessions: [Session(1, capacity: 8, peers: [Peer(5)]), Session(2)], users: [User(5)]));
        _orchestrator.Setup(o => o.GetCoordinator("a")).Returns(coordinator.Object);

        var details = _controller.GetCoordinator("a").Value!;

        Assert.Equal("a", details.Summary.Id);
        Assert.Equal(8, details.Sessions[0].Capacity);
        Assert.Null(details.Sessions[1].Capacity);
        Assert.Equal(5, Assert.Single(details.Sessions[0].Peers).UserId);
        Assert.Equal("user5", Assert.Single(details.Users).Username);
    }

    private static CoordinatorSnapshot Snapshot(string id,
        IReadOnlyList<HybridSessionSnapshot> sessions, IReadOnlyList<ConnectedUserSnapshot> users) =>
        new(id, id, "127.0.0.1", 5000, CoordinatorState.Online, LaunchedAt, sessions, users);

    private static HybridSessionSnapshot Session(int id, int capacity = SessionModel.NoCapacityLimit,
        IReadOnlyList<PeerSnapshot>? peers = null) =>
        new(id, $"session{id}", 1, capacity, WebRTCEngine.Mediasoup, false, HybridSessionNodeState.Active, peers ?? []);

    private static PeerSnapshot Peer(int userId) =>
        new(userId, HybridSessionPeerState.Connected, true, true, false, false);

    private static ConnectedUserSnapshot User(int id) =>
        new(id, $"user{id}", null, UserStatus.Online, LaunchedAt);
}
