using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Server.Application.Abstractions.Persistence;
using Server.Application.Mappers;
using Server.Application.Orchestrator;
using Server.Application.Orchestrator.Settings;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models.DataTransferObjects;
using Shared.Models.Requests.Coordinator;

namespace Server.Asp.Web.Api;

[ApiController]
[Route(PanelRoutes.Coordinator + "/bans")]
public class BansController(IOrchestratorInstance orchestrator, IBanRepository repository,
    ICoordinatorSettingsService settings) : ControllerBase
{
    public record CreateBan([property: Range(1, int.MaxValue)] int UserId, BanKind Kind,
        [property: StringLength(1000)] string? Reason = null, DateTime? ExpiresAt = null,
        [property: StringLength(45)] string? Address = null);

    [HttpGet]
    public async Task<ActionResult<List<BanDTO>>> Get(string id, bool history = false)
    {
        if (await settings.GetAsync(id) == null) return NotFound();
        var moderation = orchestrator.GetCoordinator(id)?.Moderation;
        return moderation != null ? await moderation.ListAsync(history) :
            (await repository.GetAsync(id)).Where(b => history || b.IsActive(DateTime.UtcNow)).Select(b => b.ToDto()).ToList();
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Create(string id, CreateBan ban)
    {
        var moderation = orchestrator.GetCoordinator(id)?.Moderation;
        if (moderation == null) return Problem(statusCode: 409, title: "Start the coordinator before changing bans.");
        return Answer(await moderation.SetStateAsync(UserModel.SystemUserId,
            new CoordinatorSetUserStateRequest(ban.UserId, UserState.Banned, ban.Kind, ban.Reason, ban.ExpiresAt, ban.Address)));
    }

    [HttpDelete("{banId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(string id, Guid banId)
    {
        var moderation = orchestrator.GetCoordinator(id)?.Moderation;
        if (moderation == null) return Problem(statusCode: 409, title: "Start the coordinator before changing bans.");
        return Answer(await moderation.RevokeAsync(UserModel.SystemUserId, banId));
    }

    private IActionResult Answer(ErrorCode? error) => error switch
    {
        null => NoContent(),
        ErrorCode.UserNotFound => NotFound(),
        ErrorCode.Conflict => Problem(statusCode: 409, title: "An active ban for this target already exists."),
        ErrorCode.InsufficientPermissions or ErrorCode.CannotTargetSelf => Problem(statusCode: 403, title: "This target cannot be banned."),
        _ => Problem(statusCode: 400, title: "Supply an account or IP ban, an optional reason (up to 1000 characters) and a future UTC expiry or no expiry.")
    };
}
