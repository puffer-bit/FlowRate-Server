using Server.Core.Models;
using Server.Core.Models.Roles;
using Shared.Enums;

namespace Server.Application.Coordinator.Roles;

/// <summary>What the users of this coordinator may do, after the roles they have now.</summary>
public interface ICoordinatorPermissions
{
    RoleSet Roles { get; }

    /// <summary>The permissions of a connected user; the system user has all.</summary>
    Permission PermissionsOf(int userId);

    bool Has(int userId, Permission permission);

    /// <summary>The permissions of a connected user in a session, after its overrides.</summary>
    Permission PermissionsIn(int userId, SessionModel session);

    bool HasIn(int userId, SessionModel session, Permission permission);

    /// <summary>The permissions of a user in a session, after its overrides; the user need not be connected.</summary>
    Task<Permission> PermissionsInAsync(int userId, SessionModel session);

    /// <summary>
    /// Raised after a change of the roles with the connected users whose permissions may have changed.
    /// </summary>
    event Action<IReadOnlyCollection<int>>? PermissionsChanged;

    /// <summary>Whether a connected user is frozen, and may only read.</summary>
    bool IsFrozen(int userId);

    /// <summary>The highest role position of a user, connected or not; the system user ranks above all.</summary>
    Task<int> HighestPositionOfAsync(int userId);
}
