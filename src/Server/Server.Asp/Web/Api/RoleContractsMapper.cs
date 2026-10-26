using Server.Application.Orchestrator.Roles;
using Server.Asp.Web.Api.Contracts;
using Server.Core.Models.Roles;
using Server.Core.Models.User;
using Shared.Enums;

namespace Server.Asp.Web.Api;

public static class RoleContractsMapper
{
    private static readonly Permission[] SinglePermissions = Enum.GetValues<Permission>()
        .Where(permission => permission != Permission.None && permission != Permission.All)
        .ToArray();

    public static RoleDto ToDto(this RoleModel role) => new(
        role.Id,
        role.Name,
        role.Color,
        role.Position,
        SinglePermissions.Where(permission => role.Permissions.HasFlag(permission)).ToList(),
        role.IsEveryone,
        role.Revision);

    public static IReadOnlyList<RoleDto> ToDtos(this RoleSet roles) => roles.Roles.Select(ToDto).ToList();

    public static RoleChange ToChange(this RoleChangeDto role) =>
        new(role.Name, role.Color, role.Permissions.Aggregate(Permission.None, (all, one) => all | one));

    public static CoordinatorUserDto ToCoordinatorUserDto(this UserModel user) => new(
        user.Id,
        user.Username,
        user.DisplayName,
        user.State,
        user.LastConnectionTime,
        user.RoleIds.Order().ToList());
}
