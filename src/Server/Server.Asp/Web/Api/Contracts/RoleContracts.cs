using System.ComponentModel.DataAnnotations;
using Shared.Enums;
using Shared.Enums.User;

namespace Server.Asp.Web.Api.Contracts;

/// <summary>A coordinator role. Higher <see cref="Position"/> ranks higher; the everyone role is at 0.</summary>
/// <param name="Color">0xRRGGBB, or null for no color.</param>
/// <param name="Permissions">Single permissions; Administrator allows all the others.</param>
public sealed record RoleDto(
    int Id,
    string Name,
    int? Color,
    int Position,
    IReadOnlyList<Permission> Permissions,
    bool IsEveryone,
    long Revision);

/// <summary>A new role, created right above the everyone role.</summary>
public class RoleChangeDto
{
    /// <summary>Kept as it is for the everyone role.</summary>
    [Required]
    public string Name { get; init; } = string.Empty;

    /// <summary>0xRRGGBB, or null for no color.</summary>
    public int? Color { get; init; }

    [Required]
    public IReadOnlyList<Permission> Permissions { get; init; } = [];
}

public sealed class UpdateRoleDto : RoleChangeDto
{
    /// <summary>
    /// The revision the change was made from. When the role changed since, nothing is saved and the answer is
    /// 409 Conflict.
    /// </summary>
    public long ExpectedRevision { get; init; }
}

public sealed class ReorderRolesDto
{
    /// <summary>Every role except the everyone role, lowest first.</summary>
    [Required]
    public IReadOnlyList<int> RoleIds { get; init; } = [];
}

/// <param name="RoleIds">The roles of the user, without the everyone role.</param>
public sealed record CoordinatorUserDto(
    int Id,
    string Username,
    string? DisplayName,
    UserState State,
    DateTime LastConnectionAtUtc,
    IReadOnlyList<int> RoleIds);
