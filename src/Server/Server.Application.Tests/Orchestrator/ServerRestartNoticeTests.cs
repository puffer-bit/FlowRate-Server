using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Server.Application.Coordinator;
using Server.Application.Orchestrator;
using Server.Application.Orchestrator.Restart;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Notifications.Heartbeat;
using Xunit;

namespace Server.Application.Tests.Orchestrator;

public sealed class ServerRestartNoticeTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 22, 20, 0, 0, TimeSpan.Zero));
    private readonly List<BaseMessage> _sent = [];
    private readonly ServerRestartNotice _notice;

    public ServerRestartNoticeTests()
    {
        var coordinator = new Mock<ICoordinatorInstance>();
        coordinator.Setup(c => c.Broadcast(It.IsAny<BaseMessage>())).Callback<BaseMessage>(_sent.Add);
        var orchestrator = new Mock<IOrchestratorInstance>();
        orchestrator.SetupGet(o => o.CoordinatorsPool)
            .Returns(new ConcurrentDictionary<string, ICoordinatorInstance> { ["c"] = coordinator.Object });
        _notice = new ServerRestartNotice(orchestrator.Object, _time);
    }

    private ServerRestartAnnouncement In(int minutes, ServerRestartReason reason = ServerRestartReason.Update,
        bool restartsAtEnd = false) => new(reason, _time.GetUtcNow().AddMinutes(minutes), "new version", restartsAtEnd);

    [Fact]
    public void Announcement_WarnsEveryone_AndStandsForLateUsers()
    {
        var announcement = In(5);
        _notice.Announce(announcement);

        var warning = Assert.IsType<ServerRestartNotification>(Assert.Single(_sent));
        Assert.Equal(new ServerRestartNotification(ServerRestartReason.Update, announcement.At, "new version"), warning);
        Assert.Equal(warning, _notice.Warning);
    }

    [Fact]
    public void GoingDown_TellsTheAnnouncedReason_OrARestart()
    {
        Assert.Equal(new ServerRestartNotification(ServerRestartReason.Restart), _notice.GoingDown());
        _notice.Announce(In(5, ServerRestartReason.Shutdown));
        Assert.Equal(new ServerRestartNotification(ServerRestartReason.Shutdown, null, "new version"), _notice.GoingDown());
    }

    [Fact]
    public void Cancel_TellsEveryone_AndClearsTheWarning()
    {
        Assert.False(_notice.Cancel());
        _notice.Announce(In(5));
        Assert.True(_notice.Cancel());
        Assert.IsType<ServerRestartCancelledNotification>(_sent[^1]);
        Assert.Null(_notice.Warning);
        Assert.Equal(ServerRestartReason.Restart, _notice.GoingDown().Reason);
    }

    [Fact]
    public void Due_FiresOnlyForARestartThatAsksForIt_AndNotAfterCancel()
    {
        var due = 0;
        _notice.Due += () => due++;

        _notice.Announce(In(5, ServerRestartReason.Update));
        _time.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(0, due);

        _notice.Announce(In(5, ServerRestartReason.Restart, restartsAtEnd: true));
        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(0, due);
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, due);

        _notice.Announce(In(5, ServerRestartReason.Restart, restartsAtEnd: true));
        _notice.Cancel();
        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, due);
    }

    [Fact]
    public void Announcement_ThatDoesNotStopTheServer_IsDroppedSilently_WhenItsTimeHasPassed()
    {
        _notice.Announce(In(5, ServerRestartReason.Update));
        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.Null(_notice.Warning);
        Assert.Null(_notice.Current);
        Assert.IsType<ServerRestartNotification>(Assert.Single(_sent));
    }

    [Fact]
    public void Announcement_ThatRestartsTheServer_StandsUntilItGoesDown()
    {
        _notice.Announce(In(5, ServerRestartReason.Restart, restartsAtEnd: true));
        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.NotNull(_notice.Current);
        Assert.Equal(new ServerRestartNotification(ServerRestartReason.Restart, null, "new version"), _notice.GoingDown());
    }

    [Fact]
    public void RestartNow_StopsAtOnce_WithoutAWarningFirst()
    {
        var due = 0;
        _notice.Due += () => due++;
        _notice.Announce(In(5, ServerRestartReason.Update));
        _sent.Clear();

        _notice.RestartNow("back in a minute");

        Assert.Equal(1, due);
        Assert.Empty(_sent);
        Assert.Equal(new ServerRestartNotification(ServerRestartReason.Restart, null, "back in a minute"), _notice.GoingDown());
        // The replaced announcement's timer is gone
        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, due);
    }
}
