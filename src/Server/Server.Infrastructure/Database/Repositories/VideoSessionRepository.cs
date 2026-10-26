using Microsoft.EntityFrameworkCore;
using Server.Infrastructure.Mappers;
using Server.Core.Models.HybridSession;
using Server.Application.Abstractions.Persistence;

namespace Server.Infrastructure.Database.Repositories;

public class VideoSessionRepository : IVideoSessionRepository
{
    private readonly IDbContextFactory<ServerDbContext> _contextFactory;

    public VideoSessionRepository(IDbContextFactory<ServerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<HybridSessionModel?> GetByIdAsync(
        int videoSessionId, 
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var videoSessionEntity = await context.VideoSessionsEntities
            .FirstOrDefaultAsync(x => x.Id == videoSessionId, cancellationToken);

        return videoSessionEntity?.ToModel();
    }

    public async Task<IReadOnlyCollection<HybridSessionModel>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.VideoSessionsEntities.ToListAsync(cancellationToken);
        return entities.ToModelList();
    }

    public async Task<int> AddAsync(HybridSessionModel instance,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var videoSessionEntity = instance.ToEntity();
        await context.VideoSessionsEntities.AddAsync(videoSessionEntity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        
        return videoSessionEntity.Id;
    }

    public async Task UpdateAsync(HybridSessionModel instance, 
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Only what the model owns: a whole new entity would reset CreatedAt and the runtime columns
        var entity = await context.VideoSessionsEntities
            .FirstOrDefaultAsync(x => x.Id == instance.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Hybrid session {instance.Id} is not saved.");

        entity.Name = instance.Name;
        entity.Capacity = instance.Capacity;
        entity.IsHostMustApprove = instance.IsHostMustApprove;
        entity.SetMedia(instance.Media);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(
        int videoSessionId, 
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.VideoSessionsEntities
            .FirstOrDefaultAsync(x => x.Id == videoSessionId, cancellationToken);
        
        if (entity is not null)
        {
            context.VideoSessionsEntities.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<List<HybridSessionModel>> GetByCoordinatorIdAsync(
        string coordinatorInstanceId, 
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.VideoSessionsEntities
            .Where(s => s.CoordinatorInstanceId == coordinatorInstanceId).ToListAsync(cancellationToken);
        return entities.ToModelList();
    }
}