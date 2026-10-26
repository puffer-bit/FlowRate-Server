using Server.Core.Models.Roles;
using Shared.Enums;

namespace Server.Application.Orchestrator.Roles;

/// <summary>
/// The one way to read and change coordinator roles and who has them, for the control panel and for the clients
/// alike. The actor of a change is the user it is made for; the control panel acts as the system user, which
/// may do anything.
/// </summary>
public interface IRoleService
{
    /// <summary>Raised after a change was saved, whoever made it, on the thread of the change.</summary>
    event Action<RolesChange>? RolesChanged;

    /// <summary>
    /// Loads the roles of a coordinator for <see cref="GetLoaded"/>, first creating the everyone and admin roles
    /// when it has none yet.
    /// </summary>
    Task<RoleSet> LoadAsync(string coordinatorId, CancellationToken ct = default);

    /// <summary>The roles of a coordinator loaded before; throws when they never were.</summary>
    RoleSet GetLoaded(string coordinatorId);

    /// <summary>The roles of a user of the coordinator; null when it has no such user.</summary>
    Task<IReadOnlyList<int>?> GetUserRoleIdsAsync(string coordinatorId, int userId, CancellationToken ct = default);

    Task<RoleOperationResult<RoleModel>> CreateAsync(string coordinatorId, int actorId, RoleChange change,
        CancellationToken ct = default);

    /// <summary>Changes the role unless it was changed after <paramref name="expectedRevision"/>.</summary>
    Task<RoleOperationResult<RoleModel>> UpdateAsync(string coordinatorId, int actorId, int roleId,
        RoleChange change, long expectedRevision, CancellationToken ct = default);

    Task<RoleOperationResult<RoleModel>> DeleteAsync(string coordinatorId, int actorId, int roleId,
        CancellationToken ct = default);

    /// <summary>
    /// Puts the roles in the given order, lowest first. Lists every role but the everyone role, otherwise it is
    /// a conflict.
    /// </summary>
    Task<RoleOperationResult<RoleSet>> ReorderAsync(string coordinatorId, int actorId, IReadOnlyList<int> roleIds,
        CancellationToken ct = default);

    /// <returns>The roles of the user after the change.</returns>
    Task<RoleOperationResult<IReadOnlyList<int>>> AssignAsync(string coordinatorId, int actorId, int userId,
        int roleId, CancellationToken ct = default);

    /// <returns>The roles of the user after the change.</returns>
    Task<RoleOperationResult<IReadOnlyList<int>>> UnassignAsync(string coordinatorId, int actorId, int userId,
        int roleId, CancellationToken ct = default);
}

public sealed record RoleChange(string Name, int? Color, Permission Permissions);

public enum RoleOperationStatus
{
    Done,
    InsufficientPermissions,

    /// <summary>The role or the user is not below the actor's highest role.</summary>
    HierarchyViolation,

    /// <summary>Changed by someone else meanwhile; nothing was saved.</summary>
    Conflict,
    RoleNotFound,
    UserNotFound,
    Invalid,
    TooManyRoles
}

/// <param name="Value">The result; on a conflict of an update, the role as it is now.</param>
public sealed record RoleOperationResult<T>(RoleOperationStatus Status, T? Value = default)
{
    public static RoleOperationResult<T> Failed(RoleOperationStatus status) => new(status);
}

/// <param name="Roles">All roles of the coordinator after the change.</param>
/// <param name="RolesModified">Whether a role itself changed, rather than only who has which.</param>
/// <param name="UserRoles">The users whose roles changed, with the roles they have now.</param>
public sealed record RolesChange(
    string CoordinatorId,
    RoleSet Roles,
    bool RolesModified,
    IReadOnlyDictionary<int, IReadOnlyList<int>> UserRoles);
