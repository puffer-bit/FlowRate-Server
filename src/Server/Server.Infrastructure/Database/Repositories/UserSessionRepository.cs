using Microsoft.EntityFrameworkCore;
using Server.Application.Abstractions.Persistence;
using Server.Core.Models.User;
using Server.Database.Entities;
using Server.Database;
using Shared.Enums.Auth;

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

    public async Task<UserSession?> GetAsync(string coordinatorInstanceId, Guid deviceId, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.UserSessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == deviceId && x.CoordinatorInstanceId == coordinatorInstanceId
                && x.ExpiresAt > now, cancellationToken);
        return entity == null ? null : ToModel(entity);
    }

    public async Task<bool> IsPublicKeyTakenAsync(byte[] publicKey, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.UserSessions.AnyAsync(x => x.PublicKey == publicKey, cancellationToken);
    }

    public async Task<UserSession?> FindByTokenAsync(string coordinatorInstanceId, byte[] tokenHash, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.UserSessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CoordinatorInstanceId == coordinatorInstanceId && x.ExpiresAt > now
                && x.PublicKey == null && (x.TokenHash == tokenHash || x.PreviousTokenHash == tokenHash),
                cancellationToken);
        return entity == null ? null : ToModel(entity);
    }

    public async Task<IReadOnlyList<UserSession>> GetByUserAsync(string coordinatorInstanceId, int userId,
        DateTimeOffset now, CancellationToken cancellationToken = default)
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

    public async Task<IReadOnlyList<Guid>> DeleteLegacyAsync(string coordinatorInstanceId, int userId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.UserSessions
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId && x.UserId == userId && x.PublicKey == null);
        var ids = await query.Select(x => x.Id).ToListAsync(cancellationToken);
        await query.ExecuteDeleteAsync(cancellationToken);
        return ids;
    }

    public async Task DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await context.UserSessions.Where(x => x.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task ExtendAsync(string coordinatorInstanceId, IReadOnlyCollection<Guid> deviceIds,
        DateTimeOffset now, DateTimeOffset expiresAt, DateTimeOffset temporaryExpiresAt,
        CancellationToken cancellationToken = default)
    {
        if (deviceIds.Count == 0)
            return;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var ids = deviceIds.ToList();
        await context.UserSessions
            .Where(x => ids.Contains(x.Id) && x.CoordinatorInstanceId == coordinatorInstanceId && x.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, x => x.IsTemporary ? temporaryExpiresAt : expiresAt),
                cancellationToken);
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
        LastAddress = model.LastAddress,
        PublicKey = model.PublicKey,
        IsTemporary = model.IsTemporary,
        AddedBy = (int)model.AddedBy
    };

    private static UserSession ToModel(UserSessionEntity entity) => new(entity.Id, entity.CoordinatorInstanceId,
        entity.UserId, entity.TokenHash, entity.PreviousTokenHash, entity.DeviceName, entity.DevicePlatform,
        entity.AppVersion, entity.CreatedAt, entity.LastUsedAt, entity.ExpiresAt, entity.LastAddress, entity.PublicKey,
        entity.IsTemporary, (DeviceLoginMethod)entity.AddedBy);
}
