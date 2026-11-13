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

    public async Task<ChatReadMove> MoveAsync(int userId, int chatId, int messageId, DateTime? markAt,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var entity = await context.ChatReadStates
            .FirstOrDefaultAsync(x => x.UserId == userId && x.ChatId == chatId, cancellationToken);

        var moved = false;
        if (entity == null)
        {
            entity = new ChatReadStateEntity { UserId = userId, ChatId = chatId, LastReadMessageId = messageId };
            context.ChatReadStates.Add(entity);
            moved = messageId > 0;
        }
        else if (messageId > entity.LastReadMessageId)
        {
            entity.LastReadMessageId = messageId;
            moved = true;
        }

        if (moved && markAt is { } at)
        {
            context.ChatReadMarks.Add(new ChatReadMarkEntity
            {
                ChatId = chatId, UserId = userId, UpToMessageId = messageId,
                ReadAt = DateTime.SpecifyKind(at, DateTimeKind.Utc)
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ChatReadMove(entity.LastReadMessageId, moved);
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

    public async Task<IReadOnlyDictionary<int, int>> GetPeerPositionsAsync(int userId,
        IReadOnlyCollection<int> chatIds, CancellationToken cancellationToken = default)
    {
        if (chatIds.Count == 0)
            return new Dictionary<int, int>();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ChatReadStates
            .AsNoTracking()
            .Where(x => chatIds.Contains(x.ChatId) && x.UserId != userId)
            .GroupBy(x => x.ChatId)
            .Select(g => new { ChatId = g.Key, Position = g.Max(x => x.LastReadMessageId) })
            .ToDictionaryAsync(x => x.ChatId, x => x.Position, cancellationToken);
    }

    public async Task<IReadOnlyList<ChatMessageReader>> GetReadersAsync(int chatId, int messageId, int exceptUserId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var readers = await context.ChatReadStates
            .AsNoTracking()
            .Where(x => x.ChatId == chatId && x.UserId != exceptUserId && x.LastReadMessageId >= messageId)
            .Select(x => new
            {
                x.UserId,
                ReadAt = context.ChatReadMarks
                    .Where(m => m.ChatId == chatId && m.UserId == x.UserId && m.UpToMessageId >= messageId)
                    .Min(m => (DateTime?)m.ReadAt)
            })
            .ToListAsync(cancellationToken);

        return readers
            .Select(x => new ChatMessageReader(x.UserId,
                x.ReadAt is { } at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null))
            .OrderBy(x => x.ReadAt ?? DateTime.MinValue)
            .ThenBy(x => x.UserId)
            .ToList();
    }

    public async Task<ChatReadTarget?> GetTargetAsync(int chatId, int messageId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ChatMessagesEntities
            .AsNoTracking()
            .Where(x => x.ChatId == chatId && x.Id == messageId)
            .Select(x => new ChatReadTarget(x.SenderUserId, x.IsSystem, x.IsDeleted))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
