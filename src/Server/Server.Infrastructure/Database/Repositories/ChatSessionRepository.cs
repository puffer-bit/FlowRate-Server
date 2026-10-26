using Microsoft.EntityFrameworkCore;
using Server.Core.Models.TextChat;
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

        var textChatEntity = await context.ChatSessionEntities
            .FirstOrDefaultAsync(x => x.Id == chatId, cancellationToken);

        return textChatEntity?.ToModel();
    }

    public async Task<IReadOnlyCollection<ChatSessionModel>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.ChatSessionEntities.ToListAsync(cancellationToken);
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

        var entry = context.ChatSessionEntities.Update(instance.ToEntity());
        entry.Property(x => x.LastRevision).IsModified = false;
        await context.SaveChangesAsync(cancellationToken);
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

        var entities = await context.ChatSessionEntities
            .Where(s => s.CoordinatorInstanceId == coordinatorInstanceId)
            .ToListAsync(cancellationToken);

        return entities.ToModelList();
    }
}