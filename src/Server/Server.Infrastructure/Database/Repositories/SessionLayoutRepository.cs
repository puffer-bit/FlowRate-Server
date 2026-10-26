using Microsoft.EntityFrameworkCore;
using Server.Application.Abstractions.Persistence;
using Server.Core.Models.Layout;
using Server.Infrastructure.Database.Entities;

namespace Server.Infrastructure.Database.Repositories;

public class SessionLayoutRepository : ISessionLayoutRepository
{
    private readonly IDbContextFactory<ServerDbContext> _contextFactory;

    public SessionLayoutRepository(IDbContextFactory<ServerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<SessionLayoutModel> GetAsync(string coordinatorInstanceId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var categories = await context.SessionCategories
            .AsNoTracking()
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId)
            .OrderBy(x => x.Position)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var placements = await context.SessionPlacements
            .AsNoTracking()
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId)
            .OrderBy(x => x.Position)
            .ThenBy(x => x.SessionId)
            .ToListAsync(cancellationToken);

        var categoryIds = categories.Select(x => x.Id).ToHashSet();
        var root = placements
            .Where(x => x.CategoryId is not { } id || !categoryIds.Contains(id))
            .Select(x => x.SessionId);

        return new SessionLayoutModel(root, categories.Select(category =>
            (new SessionCategoryModel(category.Id, category.Name),
                placements.Where(x => x.CategoryId == category.Id).Select(x => x.SessionId))));
    }

    public async Task<int> AddCategoryAsync(string coordinatorInstanceId, string name,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var position = await context.SessionCategories
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId)
            .MaxAsync(x => (int?)x.Position, cancellationToken) ?? -1;

        var entity = new SessionCategoryEntity
        {
            CoordinatorInstanceId = coordinatorInstanceId,
            Name = name,
            Position = position + 1
        };
        context.SessionCategories.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        return entity.Id;
    }

    public async Task SaveAsync(string coordinatorInstanceId, SessionLayoutModel layout,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var stored = await context.SessionCategories
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId)
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var position = 0;
        var kept = new HashSet<int>();
        foreach (var (category, _) in layout.Categories)
        {
            kept.Add(category.Id);
            if (stored.TryGetValue(category.Id, out var entity))
            {
                entity.Name = category.Name;
                entity.Position = position;
            }
            else
            {
                context.SessionCategories.Add(new SessionCategoryEntity
                {
                    Id = category.Id,
                    CoordinatorInstanceId = coordinatorInstanceId,
                    Name = category.Name,
                    Position = position
                });
            }
            position++;
        }

        foreach (var entity in stored.Values.Where(x => !kept.Contains(x.Id)))
            context.SessionCategories.Remove(entity);

        await context.SessionPlacements
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId)
            .ExecuteDeleteAsync(cancellationToken);

        AddPlacements(context, coordinatorInstanceId, null, layout.RootSessionIds);
        foreach (var (category, sessionIds) in layout.Categories)
            AddPlacements(context, coordinatorInstanceId, category.Id, sessionIds);

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void AddPlacements(ServerDbContext context, string coordinatorInstanceId, int? categoryId,
        IReadOnlyList<int> sessionIds)
    {
        for (var i = 0; i < sessionIds.Count; i++)
        {
            context.SessionPlacements.Add(new SessionPlacementEntity
            {
                SessionId = sessionIds[i],
                CoordinatorInstanceId = coordinatorInstanceId,
                CategoryId = categoryId,
                Position = i
            });
        }
    }
}
