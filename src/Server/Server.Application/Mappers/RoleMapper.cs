using Server.Core.Models.Roles;
using Server.Application.Orchestrator.Roles;
using Shared.Enums.Coordinator;
using Shared.Models.DataTransferObjects;

namespace Server.Application.Mappers;

public static class RoleMapperExtensions
{
    public static RoleDTO ToDto(this RoleModel role) => new()
    {
        Id = role.Id,
        Name = role.Name,
        Color = role.Color,
        Position = role.Position,
        Permissions = role.Permissions,
        IsEveryone = role.IsEveryone,
        Revision = role.Revision
    };

    public static List<RoleDTO> ToDtos(this RoleSet roles) => roles.Roles.Select(role => role.ToDto()).ToList();

    public static RoleChange ToChange(this RoleUpdateDTO role) => new(role.Name, role.Color, role.Permissions);

    public static CoordinatorRoleResult ToResult(this RoleOperationStatus status) => status switch
    {
        RoleOperationStatus.Done => CoordinatorRoleResult.NoError,
        RoleOperationStatus.InsufficientPermissions => CoordinatorRoleResult.InsufficientPermissions,
        RoleOperationStatus.HierarchyViolation => CoordinatorRoleResult.HierarchyViolation,
        RoleOperationStatus.Conflict => CoordinatorRoleResult.Conflict,
        RoleOperationStatus.RoleNotFound => CoordinatorRoleResult.RoleNotFound,
        RoleOperationStatus.UserNotFound => CoordinatorRoleResult.UserNotFound,
        RoleOperationStatus.Invalid => CoordinatorRoleResult.InvalidRole,
        RoleOperationStatus.TooManyRoles => CoordinatorRoleResult.TooManyRoles,
        _ => CoordinatorRoleResult.InternalError
    };
}
