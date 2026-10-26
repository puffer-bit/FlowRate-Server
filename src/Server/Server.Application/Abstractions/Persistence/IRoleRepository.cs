using Server.Core.Models.Roles;

namespace Server.Application.Abstractions.Persistence;

public interface IRoleRepository
{
    /// <summary>All roles of the coordinator, lowest first; empty before the first ones are added.</summary>
    Task<IReadOnlyList<RoleModel>> GetByCoordinatorAsync(string coordinatorInstanceId,
        CancellationToken cancellationToken = default);

    Task<int> AddAsync(RoleModel role, CancellationToken cancellationToken = default);

    /// <summary>Saves name, color, permissions and revision.</summary>
    Task UpdateAsync(RoleModel role, CancellationToken cancellationToken = default);

    /// <summary>Saves the positions of all the roles in one step.</summary>
    Task UpdatePositionsAsync(IEnumerable<RoleModel> roles, CancellationToken cancellationToken = default);

    /// <summary>Deletes the role and takes it from every user.</summary>
    /// <returns>The users who had it.</returns>
    Task<IReadOnlyList<int>> DeleteAsync(int roleId, CancellationToken cancellationToken = default);

    /// <summary>The roles of a user of the coordinator; null when the coordinator has no such user.</summary>
    Task<IReadOnlyList<int>?> GetUserRoleIdsAsync(string coordinatorInstanceId, int userId,
        CancellationToken cancellationToken = default);

    Task AssignAsync(int userId, int roleId, CancellationToken cancellationToken = default);
    Task UnassignAsync(int userId, int roleId, CancellationToken cancellationToken = default);
}
