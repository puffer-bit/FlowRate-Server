using Microsoft.AspNetCore.Mvc;
using Server.Application.Orchestrator;
using Server.Asp.Web.Api.Contracts;

namespace Server.Asp.Web.Api;

/// <summary>Read-only monitoring data for the browser control panel.</summary>
[ApiController]
[Route(PanelRoutes.Api)]
[Produces("application/json")]
public class PanelController(IOrchestratorInstance orchestrator) : ControllerBase
{
    [HttpGet("overview")]
    public PanelOverviewDto GetOverview()
    {
        var coordinators = orchestrator.GetCoordinatorSnapshots();

        return new PanelOverviewDto(
            orchestrator.Context.ServerVersion,
            orchestrator.Context.CurrentLaunchTime,
            coordinators.Sum(coordinator => coordinator.ActiveSessionsCount),
            coordinators.Sum(coordinator => coordinator.ConnectedUsers.Count),
            coordinators.Select(coordinator => coordinator.ToSummaryDto()).ToList());
    }

    [HttpGet("coordinators/{id}")]
    public ActionResult<CoordinatorDetailsDto> GetCoordinator(string id)
    {
        var coordinator = orchestrator.GetCoordinator(id);
        if (coordinator == null)
            return NotFound();

        return coordinator.GetSnapshot().ToDetailsDto();
    }
}
