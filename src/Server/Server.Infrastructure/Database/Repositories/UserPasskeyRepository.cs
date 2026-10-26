using Microsoft.EntityFrameworkCore;
using Server.Application.Abstractions.Persistence;
using Server.Core.Models.User;
using Server.Infrastructure.Database.Entities;
using Server.Infrastructure.Mappers;

namespace Server.Infrastructure.Database.Repositories;

public sealed class UserPasskeyRepository : IUserPasskeyRepository
{
    private readonly IDbContextFactory<ServerDbContext> _contextFactory;

    public UserPasskeyRepository(IDbContextFactory<ServerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<UserPasskey?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.Set<UserPasskeyEntity>()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return entity?.ToModel();
    }

    public async Task<UserPasskey?> GetByCredentialIdAsync(byte[] credentialId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.Set<UserPasskeyEntity>()
            .FirstOrDefaultAsync(x => x.CredentialId == credentialId, ct);
        return entity?.ToModel();
    }

    public async Task<List<UserPasskey>> GetByUserIdAsync(int userId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entities = await context.Set<UserPasskeyEntity>()
            .Where(x => x.UserId == userId)
            .ToListAsync(ct);
        return entities.Select(x => x.ToModel()).ToList();
    }

    public async Task AddAsync(UserPasskey model, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = model.ToEntity();
        await context.Set<UserPasskeyEntity>().AddAsync(entity, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(UserPasskey model, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = model.ToEntity();
        context.Set<UserPasskeyEntity>().Update(entity);
        await context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.Set<UserPasskeyEntity>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity != null)
        {
            context.Set<UserPasskeyEntity>().Remove(entity);
            await context.SaveChangesAsync(ct);
        }
    }
}