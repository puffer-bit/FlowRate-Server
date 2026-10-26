using Server.Application.Abstractions.Persistence;
using Server.Core.Models.Roles;

namespace Server.Application.Tests.Fakes;

/// <summary>Roles and who has them, kept as copies so a test sees only what was saved.</summary>
public sealed class InMemoryRoleRepository : IRoleRepository
{
    private readonly Dictionary<int, RoleModel> _roles = new();
    private readonly Dictionary<int, (string CoordinatorId, HashSet<int> RoleIds)> _users = new();
    private int _nextId = 1;

    public void AddUser(string coordinatorId, int userId, params int[] roleIds) =>
        _users[userId] = (coordinatorId, roleIds.ToHashSet());

    public IReadOnlySet<int> RolesOf(int userId) => _users[userId].RoleIds;

    public Task<IReadOnlyList<RoleModel>> GetByCoordinatorAsync(string coordinatorInstanceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RoleModel>>(_roles.Values
            .Where(role => role.CoordinatorInstanceId == coordinatorInstanceId)
            .OrderBy(role => role.Position)
            .Select(role => Copy(role))
            .ToList());

    public Task<int> AddAsync(RoleModel role, CancellationToken cancellationToken = default)
    {
        var id = _nextId++;
        _roles[id] = Copy(role, id);
        return Task.FromResult(id);
    }

    public Task UpdateAsync(RoleModel role, CancellationToken cancellationToken = default)
    {
        _roles[role.Id] = Copy(role);
        return Task.CompletedTask;
    }

    public Task UpdatePositionsAsync(IEnumerable<RoleModel> roles, CancellationToken cancellationToken = default)
    {
        foreach (var role in roles)
        {
            var saved = _roles[role.Id];
            _roles[role.Id] = new RoleModel(saved.Id, saved.CoordinatorInstanceId, saved.Name, saved.Color,
                role.Position, saved.Permissions, saved.IsEveryone, saved.Revision);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<int>> DeleteAsync(int roleId, CancellationToken cancellationToken = default)
    {
        _roles.Remove(roleId);
        var userIds = _users.Where(user => user.Value.RoleIds.Remove(roleId)).Select(user => user.Key).ToList();
        return Task.FromResult<IReadOnlyList<int>>(userIds);
    }

    public Task<IReadOnlyList<int>?> GetUserRoleIdsAsync(string coordinatorInstanceId, int userId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<int>?>(
            _users.TryGetValue(userId, out var user) && user.CoordinatorId == coordinatorInstanceId
                ? user.RoleIds.ToList()
                : null);

    public Task AssignAsync(int userId, int roleId, CancellationToken cancellationToken = default)
    {
        _users[userId].RoleIds.Add(roleId);
        return Task.CompletedTask;
    }

    public Task UnassignAsync(int userId, int roleId, CancellationToken cancellationToken = default)
    {
        _users[userId].RoleIds.Remove(roleId);
        return Task.CompletedTask;
    }

    private static RoleModel Copy(RoleModel role) => Copy(role, role.Id);

    private static RoleModel Copy(RoleModel role, int id) =>
        new(id, role.CoordinatorInstanceId, role.Name, role.Color, role.Position, role.Permissions, role.IsEveryone,
            role.Revision);
}
