using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Server.Application.Abstractions.Persistence;
using Server.Core.Models.Roles;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Validation;

namespace Server.Application.Orchestrator.Roles;

public class RoleService : IRoleService
{
    public const string AdminRoleName = "Admin";

    /// <summary>What every user may do on a new coordinator.</summary>
    public const Permission DefaultEveryonePermissions = Permission.CreateSessions | Permission.Default;

    private static readonly IReadOnlyDictionary<int, IReadOnlyList<int>> NoUsers =
        new Dictionary<int, IReadOnlyList<int>>();

    public event Action<RolesChange>? RolesChanged;

    private readonly IRoleRepository _repository;
    private readonly ILogger<RoleService> _logger;
    private readonly ConcurrentDictionary<string, RoleSet> _loaded = new();

    // Checks and saves of one change are one step, so two changes never interleave
    private readonly SemaphoreSlim _changeLock = new(1, 1);

    public RoleService(IRoleRepository repository, ILogger<RoleService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<RoleSet> LoadAsync(string coordinatorId, CancellationToken ct = default)
    {
        await _changeLock.WaitAsync(ct);
        try
        {
            return await LoadLockedAsync(coordinatorId, ct);
        }
        finally
        {
            _changeLock.Release();
        }
    }

    public RoleSet GetLoaded(string coordinatorId) =>
        _loaded.TryGetValue(coordinatorId, out var roles)
            ? roles
            : throw new InvalidOperationException($"Roles of coordinator {coordinatorId} are not loaded.");

    public Task<IReadOnlyList<int>?> GetUserRoleIdsAsync(string coordinatorId, int userId,
        CancellationToken ct = default) =>
        _repository.GetUserRoleIdsAsync(coordinatorId, userId, ct);

    public Task<RoleOperationResult<RoleModel>> CreateAsync(string coordinatorId, int actorId, RoleChange change,
        CancellationToken ct = default) =>
        ChangeAsync<RoleModel>(coordinatorId, actorId, ct, async (roles, actor) =>
        {
            if (!actor.Has(Permission.ManageRoles))
                return Failed<RoleModel>(RoleOperationStatus.InsufficientPermissions);
            if (!IsValid(change))
                return Failed<RoleModel>(RoleOperationStatus.Invalid);
            if (roles.Roles.Count >= Limits.RolesMaxCount)
                return Failed<RoleModel>(RoleOperationStatus.TooManyRoles);
            if (!actor.CanGrant(change.Permissions))
                return Failed<RoleModel>(RoleOperationStatus.HierarchyViolation);

            // A new role goes right above the everyone role, where whoever may manage roles can manage it
            var shifted = await FreshRolesAsync(coordinatorId, ct);
            foreach (var role in shifted)
                role.MoveTo(role.Position + 1);
            await _repository.UpdatePositionsAsync(shifted, ct);

            var created = RoleModel.Create(coordinatorId, change.Name, change.Color, 1, change.Permissions);
            created.Id = await _repository.AddAsync(created, ct);

            _logger.LogInformation("Role {Name} ({RoleId}) of coordinator {Id} created by {ActorId}.",
                created.Name, created.Id, coordinatorId, actorId);
            return new Outcome<RoleModel>(new(RoleOperationStatus.Done, created), true, NoUsers);
        });

    public Task<RoleOperationResult<RoleModel>> UpdateAsync(string coordinatorId, int actorId, int roleId,
        RoleChange change, long expectedRevision, CancellationToken ct = default) =>
        ChangeAsync<RoleModel>(coordinatorId, actorId, ct, async (roles, actor) =>
        {
            if (roles.Find(roleId) is not { } role)
                return Failed<RoleModel>(RoleOperationStatus.RoleNotFound);
            if (role.Revision != expectedRevision)
                return new Outcome<RoleModel>(new(RoleOperationStatus.Conflict, role), false, NoUsers);
            if (!actor.Has(Permission.ManageRoles))
                return Failed<RoleModel>(RoleOperationStatus.InsufficientPermissions);
            if (!IsValid(change, role))
                return Failed<RoleModel>(RoleOperationStatus.Invalid);
            // Permissions the role already has may stay, only new ones must be the actor's own
            if (!actor.CanManage(role) || !actor.CanGrant(change.Permissions & ~role.Permissions))
                return Failed<RoleModel>(RoleOperationStatus.HierarchyViolation);

            var changed = Copy(role);
            changed.Change(change.Name, change.Color, change.Permissions);
            await _repository.UpdateAsync(changed, ct);

            _logger.LogInformation("Role {Name} ({RoleId}) of coordinator {Id} changed by {ActorId}, revision {Revision}.",
                changed.Name, roleId, coordinatorId, actorId, changed.Revision);
            return new Outcome<RoleModel>(new(RoleOperationStatus.Done, changed), true, NoUsers);
        });

    public Task<RoleOperationResult<RoleModel>> DeleteAsync(string coordinatorId, int actorId, int roleId,
        CancellationToken ct = default) =>
        ChangeAsync<RoleModel>(coordinatorId, actorId, ct, async (roles, actor) =>
        {
            if (roles.Find(roleId) is not { } role)
                return Failed<RoleModel>(RoleOperationStatus.RoleNotFound);
            if (!actor.Has(Permission.ManageRoles))
                return Failed<RoleModel>(RoleOperationStatus.InsufficientPermissions);
            if (role.IsEveryone)
                return Failed<RoleModel>(RoleOperationStatus.Invalid);
            if (!actor.CanManage(role))
                return Failed<RoleModel>(RoleOperationStatus.HierarchyViolation);

            var userIds = await _repository.DeleteAsync(roleId, ct);

            // Close the gap, so positions stay 1, 2, 3... above the everyone role
            var remaining = await FreshRolesAsync(coordinatorId, ct);
            for (var i = 0; i < remaining.Count; i++)
                remaining[i].MoveTo(i + 1);
            await _repository.UpdatePositionsAsync(remaining, ct);

            _logger.LogInformation("Role {Name} ({RoleId}) of coordinator {Id} deleted by {ActorId}.",
                role.Name, roleId, coordinatorId, actorId);
            return new Outcome<RoleModel>(new(RoleOperationStatus.Done, role), true,
                await UserRolesAsync(coordinatorId, userIds, ct));
        });

    public async Task<RoleOperationResult<RoleSet>> ReorderAsync(string coordinatorId, int actorId,
        IReadOnlyList<int> roleIds, CancellationToken ct = default)
    {
        var result = await ChangeAsync<bool>(coordinatorId, actorId, ct, async (roles, actor) =>
        {
            if (!actor.Has(Permission.ManageRoles))
                return Failed<bool>(RoleOperationStatus.InsufficientPermissions);

            var current = roles.Roles.Where(role => !role.IsEveryone).ToList();
            if (roleIds.Count != current.Count || roleIds.Distinct().Count() != roleIds.Count
                                               || current.Any(role => !roleIds.Contains(role.Id)))
                return Failed<bool>(RoleOperationStatus.Conflict);

            var positions = roleIds.Select((id, index) => (id, position: index + 1))
                .ToDictionary(pair => pair.id, pair => pair.position);
            var moved = Fresh(current);
            foreach (var role in moved)
            {
                var position = positions[role.Id];
                if (position == role.Position)
                    continue;
                // Only roles below the actor move, and they stay below
                if (!actor.CanManage(role) || !actor.IsBelow(position))
                    return Failed<bool>(RoleOperationStatus.HierarchyViolation);
                role.MoveTo(position);
            }

            if (moved.All(role => role.Position == roles.Find(role.Id)!.Position))
                return new Outcome<bool>(new(RoleOperationStatus.Done), false, NoUsers, Changed: false);

            await _repository.UpdatePositionsAsync(moved, ct);

            _logger.LogInformation("Roles of coordinator {Id} reordered by {ActorId}.", coordinatorId, actorId);
            return new Outcome<bool>(new(RoleOperationStatus.Done, true), true, NoUsers);
        });

        return result.Status == RoleOperationStatus.Done
            ? new RoleOperationResult<RoleSet>(RoleOperationStatus.Done, GetLoaded(coordinatorId))
            : RoleOperationResult<RoleSet>.Failed(result.Status);
    }

    public Task<RoleOperationResult<IReadOnlyList<int>>> AssignAsync(string coordinatorId, int actorId, int userId,
        int roleId, CancellationToken ct = default) =>
        ChangeUserRoleAsync(coordinatorId, actorId, userId, roleId, assign: true, ct);

    public Task<RoleOperationResult<IReadOnlyList<int>>> UnassignAsync(string coordinatorId, int actorId,
        int userId, int roleId, CancellationToken ct = default) =>
        ChangeUserRoleAsync(coordinatorId, actorId, userId, roleId, assign: false, ct);

    private Task<RoleOperationResult<IReadOnlyList<int>>> ChangeUserRoleAsync(string coordinatorId, int actorId,
        int userId, int roleId, bool assign, CancellationToken ct) =>
        ChangeAsync<IReadOnlyList<int>>(coordinatorId, actorId, ct, async (roles, actor) =>
        {
            if (roles.Find(roleId) is not { } role)
                return Failed<IReadOnlyList<int>>(RoleOperationStatus.RoleNotFound);
            if (await _repository.GetUserRoleIdsAsync(coordinatorId, userId, ct) is not { } targetRoleIds)
                return Failed<IReadOnlyList<int>>(RoleOperationStatus.UserNotFound);
            if (!actor.Has(Permission.ManageRoles))
                return Failed<IReadOnlyList<int>>(RoleOperationStatus.InsufficientPermissions);
            // Every user has the everyone role; it is never given or taken
            if (role.IsEveryone)
                return Failed<IReadOnlyList<int>>(RoleOperationStatus.Invalid);
            if (!actor.CanManage(role) || (userId != actorId && !actor.Outranks(targetRoleIds)))
                return Failed<IReadOnlyList<int>>(RoleOperationStatus.HierarchyViolation);

            if (targetRoleIds.Contains(roleId) == assign)
                return new Outcome<IReadOnlyList<int>>(new(RoleOperationStatus.Done, targetRoleIds), false, NoUsers,
                    Changed: false);

            if (assign)
                await _repository.AssignAsync(userId, roleId, ct);
            else
                await _repository.UnassignAsync(userId, roleId, ct);

            var userRoleIds = assign
                ? targetRoleIds.Append(roleId).ToList()
                : targetRoleIds.Where(id => id != roleId).ToList();

            _logger.LogInformation("Role {RoleId} {Action} user {UserId} of coordinator {Id} by {ActorId}.",
                roleId, assign ? "given to" : "taken from", userId, coordinatorId, actorId);
            return new Outcome<IReadOnlyList<int>>(new(RoleOperationStatus.Done, userRoleIds), false,
                new Dictionary<int, IReadOnlyList<int>> { [userId] = userRoleIds });
        });

    /// <param name="Changed">Whether anything was saved and is to be announced.</param>
    private sealed record Outcome<T>(
        RoleOperationResult<T> Result,
        bool RolesModified,
        IReadOnlyDictionary<int, IReadOnlyList<int>> UserRoles,
        bool Changed = true);

    private static Outcome<T> Failed<T>(RoleOperationStatus status) =>
        new(RoleOperationResult<T>.Failed(status), false, NoUsers, Changed: false);

    private async Task<RoleOperationResult<T>> ChangeAsync<T>(string coordinatorId, int actorId,
        CancellationToken ct, Func<RoleSet, Actor, Task<Outcome<T>>> change)
    {
        RolesChange? announced = null;
        RoleOperationResult<T> result;

        await _changeLock.WaitAsync(ct);
        try
        {
            var roles = await LoadLockedAsync(coordinatorId, ct);

            Actor actor;
            if (actorId == UserModel.SystemUserId)
                actor = Actor.System(roles);
            else if (await _repository.GetUserRoleIdsAsync(coordinatorId, actorId, ct) is { } actorRoleIds)
                actor = new Actor(roles, actorRoleIds, false);
            else
                return RoleOperationResult<T>.Failed(RoleOperationStatus.InsufficientPermissions);

            var outcome = await change(roles, actor);
            result = outcome.Result;

            if (outcome is { Changed: true, Result.Status: RoleOperationStatus.Done })
            {
                var reloaded = outcome.RolesModified ? await ReloadAsync(coordinatorId, ct) : roles;
                announced = new RolesChange(coordinatorId, reloaded, outcome.RolesModified, outcome.UserRoles);
            }
        }
        finally
        {
            _changeLock.Release();
        }

        if (announced != null)
            RolesChanged?.Invoke(announced);

        return result;
    }

    private async Task<RoleSet> LoadLockedAsync(string coordinatorId, CancellationToken ct)
    {
        if (_loaded.TryGetValue(coordinatorId, out var loaded))
            return loaded;

        var roles = await _repository.GetByCoordinatorAsync(coordinatorId, ct);
        if (!roles.Any(role => role.IsEveryone))
        {
            await _repository.AddAsync(RoleModel.CreateEveryone(coordinatorId, DefaultEveryonePermissions), ct);
            if (roles.Count == 0)
                await _repository.AddAsync(
                    RoleModel.Create(coordinatorId, AdminRoleName, null, 1, Permission.Administrator), ct);
            _logger.LogInformation("Default roles of coordinator {Id} created.", coordinatorId);
        }

        return await ReloadAsync(coordinatorId, ct);
    }

    private async Task<RoleSet> ReloadAsync(string coordinatorId, CancellationToken ct)
    {
        var roles = new RoleSet(await _repository.GetByCoordinatorAsync(coordinatorId, ct));
        _loaded[coordinatorId] = roles;
        return roles;
    }

    /// <summary>
    /// Roles other than the everyone role as new objects, since the loaded ones are shared by readers and must
    /// not change before they are saved.
    /// </summary>
    private async Task<List<RoleModel>> FreshRolesAsync(string coordinatorId, CancellationToken ct) =>
        (await _repository.GetByCoordinatorAsync(coordinatorId, ct)).Where(role => !role.IsEveryone)
        .OrderBy(role => role.Position).ToList();

    private static List<RoleModel> Fresh(IEnumerable<RoleModel> roles) => roles.Select(Copy).ToList();

    private static RoleModel Copy(RoleModel role) =>
        new(role.Id, role.CoordinatorInstanceId, role.Name, role.Color, role.Position, role.Permissions,
            role.IsEveryone, role.Revision);

    private async Task<IReadOnlyDictionary<int, IReadOnlyList<int>>> UserRolesAsync(string coordinatorId,
        IEnumerable<int> userIds, CancellationToken ct)
    {
        var userRoles = new Dictionary<int, IReadOnlyList<int>>();
        foreach (var userId in userIds)
            userRoles[userId] = await _repository.GetUserRoleIdsAsync(coordinatorId, userId, ct) ?? [];
        return userRoles;
    }

    private static bool IsValid(RoleChange change, RoleModel? role = null) =>
        (role is { IsEveryone: true } || (RoleModel.IsValidName(change.Name) && RoleModel.IsValidColor(change.Color)))
        && RoleModel.IsValidPermissions(change.Permissions);

    /// <summary>Who makes a change, with what the roles allow them; the system user may do anything.</summary>
    private sealed record Actor(RoleSet Roles, IReadOnlyCollection<int> RoleIds, bool IsSystem)
    {
        public static Actor System(RoleSet roles) => new(roles, [], true);

        public bool Has(Permission permission) => IsSystem || Roles.Has(RoleIds, permission);
        public bool CanManage(RoleModel role) => IsSystem || Roles.CanManage(RoleIds, role);
        public bool CanGrant(Permission permissions) => IsSystem || Roles.CanGrant(RoleIds, permissions);
        public bool Outranks(IEnumerable<int> targetRoleIds) => IsSystem || Roles.Outranks(RoleIds, targetRoleIds);
        public bool IsBelow(int position) => IsSystem || position < Roles.HighestPositionOf(RoleIds);
    }
}
