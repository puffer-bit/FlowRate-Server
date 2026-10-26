using Microsoft.EntityFrameworkCore;
using Server.Core.Models.Media;
using Server.Core.Models.TextChat.Messages;
using Server.Infrastructure.Database.Entities.TextChat;
using Server.Infrastructure.Mappers;
using Server.Application.Abstractions.Persistence;

namespace Server.Infrastructure.Database.Repositories;

public class MessagesRepository : IMessagesRepository
{
    private readonly IDbContextFactory<ServerDbContext> _contextFactory;

    public MessagesRepository(IDbContextFactory<ServerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }
    
    public async Task<ChatMessageModel> AddAsync(ChatMessageModel message, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var messageEntity = message.ToEntity();
        messageEntity.Revision = await NextRevisionAsync(context, message.ChatId, cancellationToken);
        
        await context.ChatMessagesEntities.AddAsync(messageEntity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        var uploadIds = message.Images.SelectMany(image => new[] { image.UploadId, image.PreviewUploadId })
            .Concat(message.Files.Select(file => file.UploadId))
            .ToList();
        if (uploadIds.Count > 0)
        {
            var attached = await context.MediaUploads
                .Where(x => uploadIds.Contains(x.Id) && x.State == MediaUploadState.Completed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.State, MediaUploadState.Attached)
                    .SetProperty(x => x.MessageId, messageEntity.Id), cancellationToken);

            if (attached != uploadIds.Count)
                throw new MediaUploadUnavailableException();
        }

        await transaction.CommitAsync(cancellationToken);
        
        return messageEntity.ToModel(message.ReplyContent);
    }

    public async Task<List<ChatMessageModel>> GetByTakeSkipAsync(int chatId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await WithContent(context.ChatMessagesEntities)
            .Where(x => x.ChatId == chatId)
            .OrderByDescending(x => x.SentTime)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        
        return (await ToModelsAsync(context, entities, cancellationToken))
            .OrderBy(x => x.SentTime)
            .ToList();
    }

    public async Task<List<ChatMessageModel>> GetBeforeAsync(int chatId,
        int? beforeMessageId,
        int take,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = WithContent(context.ChatMessagesEntities).Where(x => x.ChatId == chatId);
        if (beforeMessageId is { } before)
            query = query.Where(x => x.Id < before);

        var entities = await query
            .OrderByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        entities.Reverse();
        return await ToModelsAsync(context, entities, cancellationToken);
    }

    public async Task<List<ChatMessageModel>> GetChangesAsync(int chatId,
        long afterRevision,
        int take,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await WithContent(context.ChatMessagesEntities)
            .IgnoreQueryFilters()
            .Where(x => x.ChatId == chatId && x.Revision > afterRevision)
            .OrderBy(x => x.Revision)
            .Take(take)
            .ToListAsync(cancellationToken);

        foreach (var entity in entities.Where(e => e.IsDeleted))
            ClearContent(entity);

        return await ToModelsAsync(context, entities, cancellationToken);
    }

    public async Task<int> CountChangesAsync(int chatId,
        long afterRevision,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.ChatMessagesEntities
            .IgnoreQueryFilters()
            .CountAsync(x => x.ChatId == chatId && x.Revision > afterRevision, cancellationToken);
    }

    public async Task<long> GetRevisionAsync(int chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.ChatSessionEntities
            .Where(x => x.Id == chatId)
            .Select(x => x.LastRevision)
            .SingleAsync(cancellationToken);
    }
    
    public async Task<ChatMessageModel?> GetByIdAsync(
        int messageId, 
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await WithContent(context.ChatMessagesEntities)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == messageId, cancellationToken);

        if (entity is null)
            return null;
        
        if (entity.IsDeleted)
            ClearContent(entity);
        
        return (await ToModelsAsync(context, [entity], cancellationToken))[0];
    }

    public async Task<IReadOnlyList<ChatMessageModel>> GetLastMessagesAsync(
        int chatId, 
        int count, 
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await WithContent(context.ChatMessagesEntities)
            .Where(x => x.ChatId == chatId)
            .OrderByDescending(x => x.SentTime)
            .Take(count)
            .ToListAsync(cancellationToken);

        entities.Reverse();
        return await ToModelsAsync(context, entities, cancellationToken);
    }

    public async Task<long?> DeleteAsync(int messageId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var entity = await context.ChatMessagesEntities
            .FirstOrDefaultAsync(x => x.Id == messageId, cancellationToken);

        if (entity is null)
            return null;

        entity.IsDeleted = true;
        entity.Revision = await NextRevisionAsync(context, entity.ChatId, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return entity.Revision;
    }

    public Task PurgeAsync(string chatId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task<long> UpdateAsync(ChatMessageModel message, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var entity = message.ToEntity();
        entity.Revision = await NextRevisionAsync(context, message.ChatId, cancellationToken);

        context.ChatMessagesEntities.Update(entity);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return entity.Revision;
    }

    private static IQueryable<ChatMessageEntity> WithContent(IQueryable<ChatMessageEntity> messages) =>
        messages
            .AsNoTracking()
            .Include(x => x.TextContent)
            .Include(x => x.Images)
            .Include(x => x.Videos)
            .Include(x => x.Files)
            .Include(x => x.Reactions);

    private static void ClearContent(ChatMessageEntity entity)
    {
        entity.TextContent = null;
        entity.Images?.Clear();
        entity.Videos?.Clear();
        entity.Files?.Clear();
        entity.Reactions?.Clear();
    }

    private static async Task<long> NextRevisionAsync(ServerDbContext context, int chatId,
        CancellationToken cancellationToken)
    {
        // Not composed further: EF would wrap the statement into a subquery, which an UPDATE cannot be
        var revisions = await context.Database
            .SqlQuery<long>($"""
                UPDATE "ChatSessions" SET "LastRevision" = "LastRevision" + 1
                WHERE "Id" = {chatId}
                RETURNING "LastRevision" AS "Value"
                """)
            .ToListAsync(cancellationToken);

        return revisions.Single();
    }

    private static async Task<List<ChatMessageModel>> ToModelsAsync(ServerDbContext context,
        List<ChatMessageEntity> entities, CancellationToken cancellationToken)
    {
        var targetIds = entities
            .Where(e => e.ReplyToMessageId.HasValue)
            .Select(e => e.ReplyToMessageId!.Value)
            .Distinct()
            .ToList();

        var targets = targetIds.Count == 0
            ? new Dictionary<int, ChatMessageEntity>()
            : await context.ChatMessagesEntities
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(x => targetIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        return entities
            .Select(e => e.ToModel(e.ReplyToMessageId is { } targetId
                ? targets.GetValueOrDefault(targetId).ToReplyContentModel(targetId)
                : null))
            .ToList();
    }
}
