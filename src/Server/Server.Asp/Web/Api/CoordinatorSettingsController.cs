using Microsoft.AspNetCore.Mvc;
using Server.Application.Orchestrator.SFUPool;
using Server.Application.Orchestrator.Settings;
using Server.Asp.Web.Api.Contracts;

namespace Server.Asp.Web.Api;

/// <summary>
/// Coordinator settings for the browser control panel. Clients change the same settings through the coordinator,
/// so both go through <see cref="ICoordinatorSettingsService"/> and every open panel hears of a change either way.
/// </summary>
[ApiController]
[Route(PanelRoutes.Api)]
[Produces("application/json")]
public class CoordinatorSettingsController(ICoordinatorSettingsService settingsService, ISfuNodeRegistry sfuNodes)
    : ControllerBase
{
    [HttpGet("coordinators/{id}/settings")]
    public async Task<ActionResult<CoordinatorSettingsDto>> GetSettings(string id, CancellationToken ct)
    {
        var model = await settingsService.GetAsync(id, ct);
        if (model == null)
            return NotFound();

        return model.ToSettingsDto();
    }

    [HttpPut("coordinators/{id}/settings")]
    [ProducesResponseType<CoordinatorSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CoordinatorSettingsDto>> UpdateSettings(string id,
        UpdateCoordinatorSettingsDto settings, CancellationToken ct)
    {
        var result = await settingsService.UpdateAsync(id, settings.ToChange(), settings.ExpectedRevision, ct);

        switch (result.Status)
        {
            case CoordinatorSettingsUpdateStatus.NotFound:
                return NotFound();

            case CoordinatorSettingsUpdateStatus.Conflict:
                return Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "The settings were changed elsewhere meanwhile.",
                    detail: "Load them again and repeat the change.");

            case CoordinatorSettingsUpdateStatus.Invalid:
                foreach (var (field, messages) in result.Errors)
                foreach (var message in messages)
                    ModelState.AddModelError(field, message);
                return ValidationProblem(ModelState);

            default:
                return result.Settings!.ToSettingsDto();
        }
    }

    /// <summary>Every stored node with the live status of its connection.</summary>
    [HttpGet("sfu-nodes")]
    public async Task<IReadOnlyList<SfuNodeDto>> GetSfuNodes(CancellationToken ct) =>
        (await settingsService.GetSfuNodesAsync(ct))
        .Select(node => node.ToDto(node.IsEnabled ? sfuNodes.GetStatus(node.Id) : null))
        .ToList();
}
