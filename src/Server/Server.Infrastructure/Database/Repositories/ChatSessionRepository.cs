using Server.Core.Models.TextChat.Messages;
using Microsoft.EntityFrameworkCore;
using Server.Core.Models.TextChat;
using Server.Infrastructure.Database.Entities.TextChat;
using Server.Infrastructure.Mappers;
using Server.Application.Abstractions.Persistence;

namespace Server.Infrastructure.Database.Repositories;

public class ChatSessionRepository : IChatSessionRepository
{
    private readonly IDbContextFactory<ServerDbContext> _contextFactory;

    public ChatSessionRepository(IDbContextFactory<ServerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<ChatSessionModel?> GetByIdAsync(
        int chatId, 
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var textChatEntity = await context.ChatSessionEntities.AsNoTracking()
            .Include(chat => chat.Members)
            .FirstOrDefaultAsync(x => x.Id == chatId, cancellationToken);

        return textChatEntity?.ToModel();
    }

    public async Task<IReadOnlyCollection<ChatSessionModel>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.ChatSessionEntities.AsNoTracking()
            .Include(chat => chat.Members).ToListAsync(cancellationToken);
        return entities.ToModelList();
    }

    public async Task<int> AddAsync(ChatSessionModel instance,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var chatEntity = instance.ToEntity();
        await context.ChatSessionEntities.AddAsync(chatEntity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        
        return chatEntity.Id;
    }

    public async Task UpdateAsync(
        ChatSessionModel instance, 
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await UpdateEntityAsync(context, instance, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatMessageModel>> UpdateWithSystemMessagesAsync(
        ChatSessionModel instance, IReadOnlyList<ChatMessageModel> messages,
        CancellationToken cancellationToken = default)
    {
        if (messages.Any(message => message.ChatId != instance.Id || !message.IsSystem
                || message.SystemContent?.IsValid() != true))
            throw new ArgumentException("Expected system messages of the updated chat.", nameof(messages));
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var entity = await UpdateEntityAsync(context, instance, cancellationToken);
        var stored = new List<ChatMessageModel>();
        foreach (var message in messages)
        {
            var entry = message.ToEntity();
            entry.Revision = ++entity.LastRevision;
            context.ChatMessagesEntities.Add(entry);
            // Assign IDs in event order, independently of EF's insertion ordering.
            await context.SaveChangesAsync(cancellationToken);
            stored.Add(entry.ToModel());
        }
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return stored;
    }

    private static async Task<ChatSessionEntity> UpdateEntityAsync(ServerDbContext context,
        ChatSessionModel instance, CancellationToken cancellationToken)
    {
        var entity = await context.ChatSessionEntities.Include(chat => chat.Members)
            .SingleAsync(chat => chat.Id == instance.Id, cancellationToken);
        var revision = entity.LastRevision;
        context.Entry(entity).CurrentValues.SetValues(instance.ToEntity());
        entity.LastRevision = revision;
        foreach (var member in entity.Members.Where(member => !instance.MemberIds.Contains(member.UserId)).ToArray())
            entity.Members.Remove(member);
        var existing = entity.Members.Select(member => member.UserId).ToHashSet();
        foreach (var userId in instance.MemberIds.Where(userId => !existing.Contains(userId)))
            entity.Members.Add(new ChatSessionMemberEntity
            {
                ChatSessionId = instance.Id,
                UserId = userId,
                AccountId = userId,
                JoinedAt = DateTime.UtcNow
            });
        return entity;
    }

    public async Task RemoveAsync(
        int chatId, 
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.ChatSessionEntities
            .FirstOrDefaultAsync(x => x.Id == chatId, cancellationToken);
        
        if (entity is not null)
        {
            context.ChatSessionEntities.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<List<ChatSessionModel>> GetByCoordinatorIdAsync(
        string coordinatorInstanceId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.ChatSessionEntities.AsNoTracking()
            .Include(chat => chat.Members)
            .Where(s => s.CoordinatorInstanceId == coordinatorInstanceId)
            .ToListAsync(cancellationToken);

        return entities.ToModelList();
    }
}