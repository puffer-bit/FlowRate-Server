using Microsoft.EntityFrameworkCore;
using Server.Application.Abstractions.Persistence;
using Server.Core.Models.Roles;
using Server.Infrastructure.Database.Entities;
using Server.Infrastructure.Mappers;

namespace Server.Infrastructure.Database.Repositories;

public class RoleRepository : IRoleRepository
{
    private readonly IDbContextFactory<ServerDbContext> _contextFactory;

    public RoleRepository(IDbContextFactory<ServerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<RoleModel>> GetByCoordinatorAsync(string coordinatorInstanceId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var roles = await context.Roles
            .AsNoTracking()
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId)
            .OrderBy(x => x.Position)
            .ToListAsync(cancellationToken);

        return roles.Select(role => role.ToModel()).ToList();
    }

    public async Task<int> AddAsync(RoleModel role, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = role.ToEntity();
        context.Roles.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        return entity.Id;
    }

    public async Task UpdateAsync(RoleModel role, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await context.Roles
            .Where(x => x.Id == role.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Name, role.Name)
                .SetProperty(x => x.Color, role.Color)
                .SetProperty(x => x.Permissions, role.Permissions)
                .SetProperty(x => x.Revision, role.Revision), cancellationToken);
    }

    public async Task UpdatePositionsAsync(IEnumerable<RoleModel> roles, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var positions = roles.ToDictionary(role => role.Id, role => role.Position);
        var entities = await context.Roles
            .Where(x => positions.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);

        foreach (var entity in entities)
            entity.Position = positions[entity.Id];

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<int>> DeleteAsync(int roleId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var userIds = await context.UserRoles
            .Where(x => x.RoleId == roleId)
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken);

        await context.UserRoles.Where(x => x.RoleId == roleId).ExecuteDeleteAsync(cancellationToken);
        await context.Roles.Where(x => x.Id == roleId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return userIds;
    }

    public async Task<IReadOnlyList<int>?> GetUserRoleIdsAsync(string coordinatorInstanceId, int userId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.UserEntities
            .AsNoTracking()
            .Where(x => x.Id == userId && x.CoordinatorInstanceId == coordinatorInstanceId)
            .Select(x => new { RoleIds = x.Roles.Select(role => role.RoleId).ToList() })
            .FirstOrDefaultAsync(cancellationToken);

        return user?.RoleIds;
    }

    public async Task AssignAsync(int userId, int roleId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        if (await context.UserRoles.AnyAsync(x => x.UserId == userId && x.RoleId == roleId, cancellationToken))
            return;

        context.UserRoles.Add(new UserRoleEntity { UserId = userId, RoleId = roleId });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UnassignAsync(int userId, int roleId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await context.UserRoles
            .Where(x => x.UserId == userId && x.RoleId == roleId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
