using Shared.Enums;
using Server.Core.Models.Roles;
using Server.Application.Orchestrator.Roles;
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

    public static ErrorCode? ToResult(this RoleOperationStatus status) => status switch
    {
        RoleOperationStatus.Done => null,
        RoleOperationStatus.InsufficientPermissions => ErrorCode.InsufficientPermissions,
        RoleOperationStatus.HierarchyViolation => ErrorCode.HierarchyViolation,
        RoleOperationStatus.Conflict => ErrorCode.Conflict,
        RoleOperationStatus.RoleNotFound => ErrorCode.RoleNotFound,
        RoleOperationStatus.UserNotFound => ErrorCode.UserNotFound,
        RoleOperationStatus.Invalid => ErrorCode.InvalidValue,
        RoleOperationStatus.TooManyRoles => ErrorCode.TooManyRoles,
        _ => ErrorCode.Internal
    };
}
