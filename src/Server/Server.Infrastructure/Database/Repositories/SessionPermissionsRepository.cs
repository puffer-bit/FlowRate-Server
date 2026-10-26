using Microsoft.EntityFrameworkCore;
using Server.Application.Abstractions.Persistence;
using Server.Infrastructure.Database.Entities;
using Shared.Permissions;

namespace Server.Infrastructure.Database.Repositories;

public class SessionPermissionsRepository : ISessionPermissionsRepository
{
    private readonly IDbContextFactory<ServerDbContext> _contextFactory;

    public SessionPermissionsRepository(IDbContextFactory<ServerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<PermissionOverride>>> GetByCoordinatorAsync(
        string coordinatorInstanceId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var overrides = await context.SessionPermissionOverrides
            .Join(context.Sessions, o => o.SessionId, s => s.Id, (o, s) => new { Override = o, s.CoordinatorInstanceId })
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId)
            .Select(x => x.Override)
            .ToListAsync(cancellationToken);

        return overrides
            .GroupBy(o => o.SessionId)
            .ToDictionary(group => group.Key,
                group => (IReadOnlyList<PermissionOverride>)group
                    .Select(o => new PermissionOverride(o.RoleId, o.Allow, o.Deny))
                    .ToList());
    }

    public async Task ReplaceAsync(int sessionId, IReadOnlyList<PermissionOverride> overrides,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await context.SessionPermissionOverrides
            .Where(o => o.SessionId == sessionId)
            .ExecuteDeleteAsync(cancellationToken);
        context.SessionPermissionOverrides.AddRange(overrides.Select(o => new SessionPermissionOverrideEntity
        {
            SessionId = sessionId,
            RoleId = o.RoleId,
            Allow = o.Allow,
            Deny = o.Deny
        }));
        await context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
