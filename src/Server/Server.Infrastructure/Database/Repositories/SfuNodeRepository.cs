using Microsoft.EntityFrameworkCore;
using Server.Core.Models.Orchestrator;
using Server.Infrastructure.Mappers;
using Server.Application.Abstractions.Persistence;

namespace Server.Infrastructure.Database.Repositories;

public class SfuNodeRepository : ISfuNodeRepository
{
    private readonly IDbContextFactory<ServerDbContext> _contextFactory;

    public SfuNodeRepository(IDbContextFactory<ServerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyCollection<SfuNodeSettingsModel>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.SfuNodes.ToListAsync(cancellationToken);
        return entities.Select(entity => entity.ToModel()).ToList();
    }

    public async Task<int> AddAsync(SfuNodeSettingsModel node, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = node.ToEntity();
        entity.Id = 0;

        await context.SfuNodes.AddAsync(entity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return entity.Id;
    }

    public async Task UpdateAsync(SfuNodeSettingsModel node, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.SfuNodes.FirstOrDefaultAsync(x => x.Id == node.Id, cancellationToken);
        if (entity == null)
            return;

        context.Entry(entity).CurrentValues.SetValues(node.ToEntity());
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await context.SfuNodes
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
