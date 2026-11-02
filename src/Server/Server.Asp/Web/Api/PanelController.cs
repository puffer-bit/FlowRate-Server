using Microsoft.AspNetCore.Mvc;
using Server.Application.Orchestrator.Settings;
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

    [HttpPost("coordinators")]
    [ProducesResponseType<CoordinatorCreatedDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CoordinatorCreatedDto>> CreateCoordinator(CreateCoordinatorDto request,
        CancellationToken ct)
    {
        var result = await orchestrator.CreateCoordinatorInstanceAsync(
            new CreateCoordinator(request.Name, request.Ip, request.Port, request.SfuNodeIds,
                request.MaxOnlineUsers, request.MaxUsers), ct);
        switch (result.Status)
        {
            case CoordinatorCreationStatus.Invalid:
                foreach (var (field, messages) in result.Errors!)
                foreach (var message in messages)
                    ModelState.AddModelError(field, message);
                return ValidationProblem(ModelState);
            case CoordinatorCreationStatus.LimitReached:
                return Problem(statusCode: StatusCodes.Status409Conflict,
                    title: $"Coordinator limit reached ({orchestrator.Context.MaxCoordinators}).");
            case CoordinatorCreationStatus.Unavailable:
                return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "The server is starting or stopping. Try again when it is online.");
            default:
                var model = result.Settings!;
                return CreatedAtAction(nameof(GetCoordinator), new { id = model.Id },
                    new CoordinatorCreatedDto(model.Id, result.State));
        }
    }

    [HttpGet("coordinators/{id}")]
    public ActionResult<CoordinatorDetailsDto> GetCoordinator(string id)
    {
        var coordinator = orchestrator.GetCoordinator(id);
        var snapshot = coordinator?.GetSnapshot()
            ?? orchestrator.GetCoordinatorSnapshots().FirstOrDefault(item => item.Id == id);
        if (snapshot == null)
            return NotFound();

        return snapshot.ToDetailsDto();
    }
}
