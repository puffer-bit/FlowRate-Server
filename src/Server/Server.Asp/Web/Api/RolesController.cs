using Microsoft.AspNetCore.Mvc;
using Server.Application.Abstractions.Persistence;
using Server.Application.Orchestrator.Roles;
using Server.Application.Orchestrator.Settings;
using Server.Asp.Web.Api.Contracts;
using Server.Core.Models.User;

namespace Server.Asp.Web.Api;

/// <summary>
/// Coordinator roles and who has them, for the browser control panel. The panel acts as the system user, which
/// may do anything; it is the way to make the first administrator. Clients change the same roles through the
/// coordinator, so both go through <see cref="IRoleService"/>.
/// </summary>
[ApiController]
[Route(PanelRoutes.Coordinator)]
[Produces("application/json")]
public class RolesController(
    IRoleService roleService,
    ICoordinatorSettingsService settingsService,
    IUserRepository userRepository) : ControllerBase
{
    private const int Panel = UserModel.SystemUserId;

    /// <summary>All roles, lowest first.</summary>
    [HttpGet("roles")]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> GetRoles(string id, CancellationToken ct)
    {
        if (!await ExistsAsync(id, ct))
            return NotFound();

        return Ok((await roleService.LoadAsync(id, ct)).ToDtos());
    }

    [HttpPost("roles")]
    [ProducesResponseType<RoleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RoleDto>> CreateRole(string id, RoleChangeDto role, CancellationToken ct)
    {
        if (!await ExistsAsync(id, ct))
            return NotFound();

        var result = await roleService.CreateAsync(id, Panel, role.ToChange(), ct);
        return result.Status == RoleOperationStatus.Done ? result.Value!.ToDto() : Failure(result.Status);
    }

    [HttpPut("roles/{roleId:int}")]
    [ProducesResponseType<RoleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoleDto>> UpdateRole(string id, int roleId, UpdateRoleDto role,
        CancellationToken ct)
    {
        if (!await ExistsAsync(id, ct))
            return NotFound();

        var result = await roleService.UpdateAsync(id, Panel, roleId, role.ToChange(), role.ExpectedRevision, ct);
        return result.Status == RoleOperationStatus.Done ? result.Value!.ToDto() : Failure(result.Status);
    }

    /// <summary>Deletes the role and takes it from every user who has it.</summary>
    [HttpDelete("roles/{roleId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteRole(string id, int roleId, CancellationToken ct)
    {
        if (!await ExistsAsync(id, ct))
            return NotFound();

        var result = await roleService.DeleteAsync(id, Panel, roleId, ct);
        return result.Status == RoleOperationStatus.Done ? NoContent() : Failure(result.Status);
    }

    /// <summary>Puts the roles in a new order; answers with all roles, lowest first.</summary>
    [HttpPut("roles/order")]
    [ProducesResponseType<IReadOnlyList<RoleDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> ReorderRoles(string id, ReorderRolesDto order,
        CancellationToken ct)
    {
        if (!await ExistsAsync(id, ct))
            return NotFound();

        var result = await roleService.ReorderAsync(id, Panel, order.RoleIds, ct);
        return result.Status == RoleOperationStatus.Done ? Ok(result.Value!.ToDtos()) : Failure(result.Status);
    }

    /// <summary>Every user of the coordinator, with their roles.</summary>
    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<CoordinatorUserDto>>> GetUsers(string id, CancellationToken ct)
    {
        if (!await ExistsAsync(id, ct))
            return NotFound();

        return Ok((await userRepository.GetAllAsync(ct)).Values
            .Where(user => user.CoordinatorInstanceId == id)
            .OrderBy(user => user.Username, StringComparer.OrdinalIgnoreCase)
            .Select(user => user.ToCoordinatorUserDto())
            .ToList());
    }

    /// <summary>Gives a role to a user; answers with the roles the user has now.</summary>
    [HttpPut("users/{userId:int}/roles/{roleId:int}")]
    [ProducesResponseType<IReadOnlyList<int>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<int>>> AssignRole(string id, int userId, int roleId,
        CancellationToken ct)
    {
        if (!await ExistsAsync(id, ct))
            return NotFound();

        var result = await roleService.AssignAsync(id, Panel, userId, roleId, ct);
        return result.Status == RoleOperationStatus.Done ? Ok(result.Value!.Order().ToList()) : Failure(result.Status);
    }

    /// <summary>Takes a role from a user; answers with the roles the user has now.</summary>
    [HttpDelete("users/{userId:int}/roles/{roleId:int}")]
    [ProducesResponseType<IReadOnlyList<int>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<int>>> UnassignRole(string id, int userId, int roleId,
        CancellationToken ct)
    {
        if (!await ExistsAsync(id, ct))
            return NotFound();

        var result = await roleService.UnassignAsync(id, Panel, userId, roleId, ct);
        return result.Status == RoleOperationStatus.Done ? Ok(result.Value!.Order().ToList()) : Failure(result.Status);
    }

    private async Task<bool> ExistsAsync(string id, CancellationToken ct) =>
        await settingsService.GetAsync(id, ct) != null;

    private ObjectResult Failure(RoleOperationStatus status) => status switch
    {
        RoleOperationStatus.RoleNotFound => Problem(statusCode: StatusCodes.Status404NotFound,
            title: "There is no such role."),
        RoleOperationStatus.UserNotFound => Problem(statusCode: StatusCodes.Status404NotFound,
            title: "The coordinator has no such user."),
        RoleOperationStatus.Conflict => Problem(statusCode: StatusCodes.Status409Conflict,
            title: "The roles were changed elsewhere meanwhile.",
            detail: "Load them again and repeat the change."),
        RoleOperationStatus.TooManyRoles => Problem(statusCode: StatusCodes.Status400BadRequest,
            title: "The coordinator has as many roles as it may have."),
        RoleOperationStatus.Invalid => Problem(statusCode: StatusCodes.Status400BadRequest,
            title: "The role cannot be like that.",
            detail: "Names are 1 to 32 characters and not @everyone; the everyone role cannot be deleted, " +
                    "given or taken."),
        _ => Problem(statusCode: StatusCodes.Status403Forbidden, title: "Not allowed.")
    };
}
