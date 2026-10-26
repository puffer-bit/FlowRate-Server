using Microsoft.EntityFrameworkCore;
using Server.Core.Models.Coordinator;
using Server.Infrastructure.Database.Entities;
using Server.Infrastructure.Mappers;
using Server.Application.Abstractions.Persistence;

namespace Server.Infrastructure.Database.Repositories;

public class CoordinatorInstanceRepository : ICoordinatorInstanceRepository
{
    private readonly IDbContextFactory<ServerDbContext> _contextFactory;

    public CoordinatorInstanceRepository(IDbContextFactory<ServerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<CoordinatorInstanceModel?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var coordinatorInstanceEntity = await context.CoordinatorInstances
            .Include(x => x.SfuNodes)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return coordinatorInstanceEntity?.ToModel();
    }

    public async Task<IReadOnlyCollection<CoordinatorInstanceModel>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var coordinatorInstanceEntityList = await context.CoordinatorInstances
            .Include(x => x.SfuNodes)
            .ToListAsync(cancellationToken);

        return coordinatorInstanceEntityList.ToModelList();
    }

    public async Task AddAsync(CoordinatorInstanceModel model, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = model.ToEntity();
        entity.SfuNodes = await LoadSfuNodesAsync(context, model.SfuNodeIds, cancellationToken);

        await context.CoordinatorInstances.AddAsync(entity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(CoordinatorInstanceModel model, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.CoordinatorInstances
            .Include(x => x.SfuNodes)
            .FirstOrDefaultAsync(x => x.Id == model.Id, cancellationToken);

        if (entity == null)
            return;

        var updated = model.ToEntity();
        context.Entry(entity).CurrentValues.SetValues(updated);
        entity.AllowedIPs = updated.AllowedIPs;
        entity.BannedIPs = updated.BannedIPs;

        entity.SfuNodes.Clear();
        entity.SfuNodes.AddRange(await LoadSfuNodesAsync(context, model.SfuNodeIds, cancellationToken));

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(string id, CancellationToken token = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(token);

        await context.CoordinatorInstances
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(token);
    }

    private static async Task<List<SfuNodeEntity>> LoadSfuNodesAsync(ServerDbContext context,
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return new List<SfuNodeEntity>();

        return await context.SfuNodes
            .Where(node => ids.Contains(node.Id))
            .ToListAsync(cancellationToken);
    }
}
