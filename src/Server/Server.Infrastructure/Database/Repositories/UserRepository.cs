using Microsoft.EntityFrameworkCore;
using Shared.Enums.User;
using Server.Core.Models.User;
using Server.Database.Entities;
using Server.Database;
using Server.Infrastructure.Mappers;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Authentication;

namespace Server.Infrastructure.Database.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IDbContextFactory<ServerDbContext> _contextFactory;

    public UserRepository(IDbContextFactory<ServerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<UserModel?> GetByIdAsync(
        int userId, 
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var userEntity = await context.UserEntities
            .Include(x => x.Roles)
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        
        return userEntity.ToModel();
    }
    
    public async Task<UserModel?> GetByIdentityAsync(
        string coordinatorInstanceId,
        string identity, 
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var identityHash = SecretHash.Of(identity);
        var userEntity = await context.UserEntities
            .Include(x => x.Roles)
            .FirstOrDefaultAsync(x => x.IdentityHash == identityHash && x.CoordinatorInstanceId == coordinatorInstanceId,
                cancellationToken);
        
        return userEntity.ToModel();
    }

    public async Task<UserModel?> GetByRecoveryKeyHashAsync(string coordinatorInstanceId, byte[] recoveryKeyHash,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var userEntity = await context.UserEntities
            .Include(x => x.Roles)
            .FirstOrDefaultAsync(x => x.RecoveryKeyHash == recoveryKeyHash
                && x.CoordinatorInstanceId == coordinatorInstanceId, cancellationToken);
        return userEntity?.ToModel();
    }

    public async Task<UserModel?> GetByUsernameAsync(string coordinatorInstanceId, string username,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var name = username.Trim().ToLower();
        var found = await context.UserEntities
            .Include(x => x.Roles)
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId && x.Username.ToLower() == name)
            .Take(2)
            .ToListAsync(cancellationToken);
        // Names taken before they were compared ignoring case may still clash; such a name finds no one
        return found.Count == 1 ? found[0].ToModel() : null;
    }

    public async Task<IDictionary<int, UserModel>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var users = await context.UserEntities
            .AsNoTracking()
            .Include(x => x.Roles)
            .ToListAsync(cancellationToken);

        return users.ToDictionary(entity => entity.Id).ToModelDictionary();
    }

    public async Task<IReadOnlySet<string>> GetUsernamesStartingWithAsync(string prefix,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var usernames = await context.UserEntities
            .Where(entity => entity.Username.StartsWith(prefix))
            .Select(entity => entity.Username)
            .ToListAsync(cancellationToken);

        return usernames.ToHashSet();
    }

    public async Task<bool> IsUsernameTakenAsync(string username, int exceptUserId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.UserEntities
            .AnyAsync(entity => entity.Username.ToLower() == username.ToLower() && entity.Id != exceptUserId,
                cancellationToken);
    }

    public async Task<int> CountAsync(string coordinatorInstanceId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.UserEntities
            .CountAsync(entity => entity.CoordinatorInstanceId == coordinatorInstanceId
                                  && entity.Kind == UserKind.User, cancellationToken);
    }

    public async Task<int> CountBotsAsync(string coordinatorInstanceId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.UserEntities
            .CountAsync(entity => entity.CoordinatorInstanceId == coordinatorInstanceId
                                  && entity.Kind == UserKind.Bot, cancellationToken);
    }

    public async Task<IReadOnlyList<UserModel>> GetBotsAsync(string coordinatorInstanceId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var bots = await context.UserEntities
            .AsNoTracking()
            .Include(x => x.Roles)
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId
                        && (x.Kind == UserKind.Bot || x.Kind == UserKind.DebugBot))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        return bots.Select(entity => entity.ToModel()).ToList();
    }

    public async Task<IReadOnlyList<int>> ClearBotOwnerAsync(int ownerUserId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var ids = await context.UserEntities
            .Where(x => x.OwnerUserId == ownerUserId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (ids.Count > 0)
            await context.UserEntities
                .Where(x => ids.Contains(x.Id))
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.OwnerUserId, (int?)null), cancellationToken);
        return ids;
    }

    public async Task<int> AddAsync(UserModel model, UserRecoveryKeys? recoveryKeys,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var userEntity = model.ToEntity(null);
        userEntity.RecoveryKeyHash = recoveryKeys?.RecoveryKeyHash;
        userEntity.AccountPublicKey = recoveryKeys?.AccountPublicKey;
        userEntity.SecurityData = new UserSecurityDataEntity();
        await context.UserEntities.AddAsync(userEntity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        
        return userEntity.Id;
    }

    public async Task UpdateAsync(UserEntity entity, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        context.UserEntities.Update(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(UserModel model, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.UserEntities
            .FirstOrDefaultAsync(x => x.Id == model.Id, cancellationToken);
        
        if (entity == null)
            return;
        
        // What the model does not hold stays as it is
        var values = model.ToEntity(entity.IdentityHash);
        values.RecoveryKeyHash = entity.RecoveryKeyHash;
        values.AccountPublicKey = entity.AccountPublicKey;
        context.Entry(entity).CurrentValues.SetValues(values);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(
        int userId, 
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.UserEntities
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (entity is not null)
        {
            context.UserEntities.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task SetRecoveryKeysAsync(int userId, UserRecoveryKeys recoveryKeys,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await context.UserEntities
            .Where(x => x.Id == userId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RecoveryKeyHash, recoveryKeys.RecoveryKeyHash)
                .SetProperty(x => x.AccountPublicKey, recoveryKeys.AccountPublicKey)
                .SetProperty(x => x.IdentityHash, (byte[]?)null), cancellationToken);
    }

    public async Task<bool> IsRecoveryKeyTakenAsync(byte[] recoveryKeyHash, int exceptUserId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.UserEntities.AnyAsync(x => x.RecoveryKeyHash == recoveryKeyHash && x.Id != exceptUserId,
            cancellationToken);
    }
}
