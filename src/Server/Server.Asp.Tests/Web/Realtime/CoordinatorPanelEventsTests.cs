using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Users;
using Server.Asp.Web.Realtime;
using Server.Core.Models.HybridSession;
using Server.Core.Models.User;
using Shared.Enums;

namespace Server.Asp.Tests.Web.Realtime;

public class CoordinatorPanelEventsTests
{
    private const string CoordinatorId = "coordinator";

    private readonly Mock<IPanelNotifier> _notifier = new();
    private readonly CoordinatorPanelEvents _events;
    private readonly CoordinatorEventBus _bus;

    public CoordinatorPanelEventsTests()
    {
        _events = new CoordinatorPanelEvents(CoordinatorId, _notifier.Object);
        _bus = new CoordinatorEventBus(NullLogger.Instance, [_events]);
    }

    [Fact]
    public async Task HybridSessionEvent_PublishedOnTheBus_NotifiesThePanel()
    {
        var peer = new PeerSnapshot(1, HybridSessionPeerState.Connected, true, true, false, false);

        await _bus.PublishAsync(new HybridSessionPeerJoinedEvent(10, peer));

        _notifier.Verify(n => n.NotifyCoordinatorChanged(CoordinatorId), Times.Once);
    }

    [Fact]
    public async Task SessionsEvent_PublishedOnTheBus_NotifiesThePanel()
    {
        await _bus.PublishAsync(new HybridSessionDeletedEvent(10, 1));

        _notifier.Verify(n => n.NotifyCoordinatorChanged(CoordinatorId), Times.Once);
    }

    [Fact]
    public async Task ChatMessageEvent_DoesNotNotifyThePanel()
    {
        await _bus.PublishAsync(new ChatSessionTypingEvent(10, 1));

        _notifier.Verify(n => n.NotifyCoordinatorChanged(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void WatchedUserManager_ConnectUpdateAndDisconnect_NotifyThePanel()
    {
        var users = new Mock<IUserManager>();
        _events.Watch(users.Object);

        var alice = UserModel.Create(CoordinatorId, "alice", null, DateTime.UtcNow);
        users.Raise(u => u.UserConnected += null, alice);
        users.Raise(u => u.UserUpdated += null, alice);
        users.Raise(u => u.UserDisconnected += null, alice, "bye");

        _notifier.Verify(n => n.NotifyCoordinatorChanged(CoordinatorId), Times.Exactly(3));
    }
}
