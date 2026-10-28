using Microsoft.EntityFrameworkCore;
using Server.Application.Abstractions.Persistence;
using Server.Core.Models.User;
using Server.Infrastructure.Database.Entities;

namespace Server.Infrastructure.Database.Repositories;

public class UserSessionRepository(IDbContextFactory<ServerDbContext> contextFactory) : IUserSessionRepository
{
    public async Task AddAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        context.UserSessions.Add(ToEntity(session));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        context.UserSessions.Update(ToEntity(session));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<UserSession?> FindByTokenAsync(string coordinatorInstanceId, byte[] tokenHash, DateTime now,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.UserSessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CoordinatorInstanceId == coordinatorInstanceId && x.ExpiresAt > now
                && (x.TokenHash == tokenHash || x.PreviousTokenHash == tokenHash), cancellationToken);
        return entity == null ? null : ToModel(entity);
    }

    public async Task<IReadOnlyList<UserSession>> GetByUserAsync(string coordinatorInstanceId, int userId,
        DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.UserSessions.AsNoTracking()
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId && x.UserId == userId && x.ExpiresAt > now)
            .OrderByDescending(x => x.LastUsedAt)
            .ToListAsync(cancellationToken);
        return entities.Select(ToModel).ToList();
    }

    public async Task<bool> DeleteAsync(string coordinatorInstanceId, int userId, Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.UserSessions
            .Where(x => x.Id == sessionId && x.CoordinatorInstanceId == coordinatorInstanceId && x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyList<Guid>> DeleteAllButAsync(string coordinatorInstanceId, int userId, Guid? keep,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.UserSessions
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId && x.UserId == userId && x.Id != keep);
        var ids = await query.Select(x => x.Id).ToListAsync(cancellationToken);
        await query.ExecuteDeleteAsync(cancellationToken);
        return ids;
    }

    public async Task DeleteExpiredAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await context.UserSessions.Where(x => x.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);
    }

    private static UserSessionEntity ToEntity(UserSession model) => new()
    {
        Id = model.Id,
        CoordinatorInstanceId = model.CoordinatorInstanceId,
        UserId = model.UserId,
        TokenHash = model.TokenHash,
        PreviousTokenHash = model.PreviousTokenHash,
        DeviceName = model.DeviceName,
        DevicePlatform = model.DevicePlatform,
        AppVersion = model.AppVersion,
        CreatedAt = model.CreatedAt,
        LastUsedAt = model.LastUsedAt,
        ExpiresAt = model.ExpiresAt,
        LastAddress = model.LastAddress
    };

    private static UserSession ToModel(UserSessionEntity entity) => new(entity.Id, entity.CoordinatorInstanceId,
        entity.UserId, entity.TokenHash, entity.PreviousTokenHash, entity.DeviceName, entity.DevicePlatform,
        entity.AppVersion, entity.CreatedAt, entity.LastUsedAt, entity.ExpiresAt, entity.LastAddress);
}
