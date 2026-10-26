using Microsoft.EntityFrameworkCore;
using Server.Core.Models.User;
using Server.Infrastructure.Database.Entities;
using Server.Infrastructure.Mappers;
using Server.Application.Abstractions.Persistence;

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

        var userEntity = await context.UserEntities
            .Include(x => x.Roles)
            .FirstOrDefaultAsync(x => x.UserIdentity == identity && x.CoordinatorInstanceId == coordinatorInstanceId,
                cancellationToken);
        
        return userEntity.ToModel();
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
            .AnyAsync(entity => entity.Username == username && entity.Id != exceptUserId, cancellationToken);
    }

    public async Task<int> CountAsync(string coordinatorInstanceId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.UserEntities
            .CountAsync(entity => entity.CoordinatorInstanceId == coordinatorInstanceId, cancellationToken);
    }

    public async Task<int> AddAsync(UserModel model, string userIdentity, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var userEntity = model.ToEntity(userIdentity);
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
        
        context.Entry(entity).CurrentValues.SetValues(model.ToEntity(entity.UserIdentity));
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

    public async Task<string?> GetIdentityByIdAsync(
        int userId, 
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.UserEntities
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        return entity?.UserIdentity;
    }
}