using Microsoft.EntityFrameworkCore;
using Server.Application.Abstractions.Persistence;
using Server.Core.Models.TextChat.Messages.Content;
using Server.Infrastructure.Mappers;

namespace Server.Infrastructure.Database.Repositories;

public class LinkPreviewRepository : ILinkPreviewRepository
{
    private readonly IDbContextFactory<ServerDbContext> _contextFactory;

    public LinkPreviewRepository(IDbContextFactory<ServerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<ChatLinkPreviewModel?> GetNewestAsync(string coordinatorInstanceId, string url,
        DateTime fetchedAfter, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.LinkPreviews
            .AsNoTracking()
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId && x.Url == url && x.FetchedAt >= fetchedAfter)
            .OrderByDescending(x => x.FetchedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return entity?.ToModel();
    }

    public async Task<ChatLinkPreviewModel?> GetAsync(string coordinatorInstanceId, int id,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.LinkPreviews
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.CoordinatorInstanceId == coordinatorInstanceId, cancellationToken);
        return entity?.ToModel();
    }

    public async Task<ChatLinkPreviewModel> AddAsync(ChatLinkPreviewModel preview,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = preview.ToEntity();
        entity.Id = 0;
        context.LinkPreviews.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entity.ToModel();
    }

    public async Task<IReadOnlyList<int>> RemoveUnusedAsync(string coordinatorInstanceId, DateTime fetchedBefore,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // Deleted messages count too: they are filtered out by default, yet still hold the key
        var unused = await context.LinkPreviews
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId && x.FetchedAt < fetchedBefore
                && !context.ChatMessagesEntities.IgnoreQueryFilters().Any(m => m.LinkPreviewId == x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (unused.Count == 0)
            return [];

        await context.LinkPreviews.Where(x => unused.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);
        return unused;
    }
}
