using Microsoft.EntityFrameworkCore;
using Server.Application.Abstractions.Persistence;
using Server.Core.Models.User;
using Server.Infrastructure.Database.Entities;
using Server.Infrastructure.Mappers;

namespace Server.Infrastructure.Database.Repositories;

public sealed class UserSecurityDataRepository : IUserSecurityDataRepository
{
    private readonly IDbContextFactory<ServerDbContext> _contextFactory;

    public UserSecurityDataRepository(IDbContextFactory<ServerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<UserSecurityData?> GetByUserIdAsync(int userId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.Set<UserSecurityDataEntity>()
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);
        
        return entity?.ToModel();
    }

    public async Task AddAsync(UserSecurityData model, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = model.ToEntity();
        await context.Set<UserSecurityDataEntity>().AddAsync(entity, ct);
        await context.SaveChangesAsync(ct);
    }

    /// <summary>Saves the data, adding it for users made before it was kept.</summary>
    public async Task UpdateAsync(UserSecurityData model, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var existingEntity = await context.Set<UserSecurityDataEntity>()
            .FirstOrDefaultAsync(x => x.UserId == model.UserId, ct);

        if (existingEntity != null)
            existingEntity.CopyFrom(model);
        else
            await context.Set<UserSecurityDataEntity>().AddAsync(model.ToEntity(), ct);

        await context.SaveChangesAsync(ct);
    }
}