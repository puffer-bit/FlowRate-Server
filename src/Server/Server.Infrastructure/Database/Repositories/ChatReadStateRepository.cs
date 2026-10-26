using Microsoft.EntityFrameworkCore;
using Server.Application.Abstractions.Persistence;
using Server.Core.Models.TextChat;
using Server.Infrastructure.Database.Entities.TextChat;

namespace Server.Infrastructure.Database.Repositories;

public class ChatReadStateRepository : IChatReadStateRepository
{
    private readonly IDbContextFactory<ServerDbContext> _contextFactory;

    public ChatReadStateRepository(IDbContextFactory<ServerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyDictionary<int, int>> GetPositionsAsync(int userId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ChatReadStates
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToDictionaryAsync(x => x.ChatId, x => x.LastReadMessageId, cancellationToken);
    }

    public async Task<int> MoveAsync(int userId, int chatId, int messageId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var entity = await context.ChatReadStates
            .FirstOrDefaultAsync(x => x.UserId == userId && x.ChatId == chatId, cancellationToken);

        if (entity == null)
        {
            entity = new ChatReadStateEntity { UserId = userId, ChatId = chatId, LastReadMessageId = messageId };
            context.ChatReadStates.Add(entity);
        }
        else if (messageId > entity.LastReadMessageId)
        {
            entity.LastReadMessageId = messageId;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return entity.LastReadMessageId;
    }

    public async Task<IReadOnlyList<ChatUnreadMessage>> GetUnreadAsync(int chatId, int userId, int afterMessageId,
        int take, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ChatMessagesEntities
            .AsNoTracking()
            .Where(x => x.ChatId == chatId && x.Id > afterMessageId && !x.IsSystem && x.SenderUserId != userId)
            .OrderBy(x => x.Id)
            .Take(take)
            .Select(x => new ChatUnreadMessage(x.Id, x.TextContent != null ? x.TextContent.Text : null))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetLastMessageIdAsync(int chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ChatMessagesEntities
            .AsNoTracking()
            .Where(x => x.ChatId == chatId)
            .MaxAsync(x => (int?)x.Id, cancellationToken) ?? 0;
    }
}
