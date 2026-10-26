using Moq;
using Server.Application.Orchestrator;
using Server.Asp.Web.Realtime;

namespace Server.Asp.Tests.Web.Realtime;

public class OrchestratorPanelEventsTests
{
    private readonly Mock<IOrchestratorInstance> _orchestrator = new();
    private readonly Mock<IPanelNotifier> _notifier = new();
    private readonly OrchestratorPanelEvents _events;

    public OrchestratorPanelEventsTests()
    {
        _events = new OrchestratorPanelEvents(_orchestrator.Object, _notifier.Object);
    }

    [Fact]
    public async Task CoordinatorChange_WhileStarted_NotifiesThePanel()
    {
        await _events.StartAsync(CancellationToken.None);

        _orchestrator.Raise(o => o.CoordinatorChanged += null, "coordinator");

        _notifier.Verify(n => n.NotifyCoordinatorChanged("coordinator"), Times.Once);
    }

    [Fact]
    public async Task CoordinatorChange_AfterStop_IsNotSignalled()
    {
        await _events.StartAsync(CancellationToken.None);
        await _events.StopAsync(CancellationToken.None);

        _orchestrator.Raise(o => o.CoordinatorChanged += null, "coordinator");

        _notifier.Verify(n => n.NotifyCoordinatorChanged(It.IsAny<string>()), Times.Never);
    }
}
