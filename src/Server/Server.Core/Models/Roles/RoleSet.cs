using Shared.Enums;

namespace Server.Core.Models.Roles;

/// <summary>
/// All roles of one coordinator, lowest first, and the rules that follow from them. Users are given here only
/// by the ids of their roles; the everyone role counts for every user without being listed.
/// </summary>
public sealed class RoleSet
{
    public IReadOnlyList<RoleModel> Roles { get; }
    public RoleModel Everyone { get; }

    private readonly Dictionary<int, RoleModel> _byId;

    public RoleSet(IEnumerable<RoleModel> roles)
    {
        Roles = roles.OrderBy(role => role.Position).ToList();
        Everyone = Roles.SingleOrDefault(role => role.IsEveryone)
                   ?? throw new ArgumentException("A coordinator has exactly one everyone role.", nameof(roles));
        _byId = Roles.ToDictionary(role => role.Id);
    }

    public RoleModel? Find(int roleId) => _byId.GetValueOrDefault(roleId);

    /// <summary>The highest position among the roles; 0 for the everyone role alone.</summary>
    public int HighestPositionOf(IEnumerable<int> roleIds) =>
        Known(roleIds).Select(role => role.Position).DefaultIfEmpty(RoleModel.EveryonePosition).Max();

    /// <summary>What a user with these roles may do; <see cref="Permission.Administrator"/> means everything.</summary>
    public Permission PermissionsOf(IEnumerable<int> roleIds)
    {
        var permissions = Known(roleIds).Aggregate(Everyone.Permissions, (all, role) => all | role.Permissions);
        return permissions.HasFlag(Permission.Administrator) ? Permission.All : permissions;
    }

    public bool Has(IEnumerable<int> roleIds, Permission permission) =>
        (PermissionsOf(roleIds) & permission) == permission;

    /// <summary>
    /// Whether a user may act on another one, as kick or rename: only on users whose highest role is lower than
    /// their own. Administrators too, so two users with the same highest role cannot act on each other.
    /// </summary>
    public bool Outranks(IEnumerable<int> actorRoleIds, IEnumerable<int> targetRoleIds) =>
        HighestPositionOf(actorRoleIds) > HighestPositionOf(targetRoleIds);

    /// <summary>Whether a user may change, move, give or take a role: only one below their highest role.</summary>
    public bool CanManage(IEnumerable<int> actorRoleIds, RoleModel role) =>
        role.Position < HighestPositionOf(actorRoleIds);

    /// <summary>A user cannot give a role permissions they do not have themselves.</summary>
    public bool CanGrant(IEnumerable<int> actorRoleIds, Permission permissions) =>
        (permissions & ~PermissionsOf(actorRoleIds)) == 0;

    private IEnumerable<RoleModel> Known(IEnumerable<int> roleIds) =>
        roleIds.Select(Find).OfType<RoleModel>();
}
