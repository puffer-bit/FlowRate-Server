using Microsoft.EntityFrameworkCore;
using Server.Core.Models;
using Server.Infrastructure.Mappers;
using Server.Application.Abstractions.Persistence;

namespace Server.Infrastructure.Database.Repositories;

public class SessionsRepository : ISessionsRepository
{
    private readonly IDbContextFactory<ServerDbContext> _contextFactory;

    public SessionsRepository(IDbContextFactory<ServerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }
    
    public async Task<IReadOnlyList<SessionModel>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var allSessionEntities = await context.Sessions
            .ToListAsync();

        return allSessionEntities
            .Select(entity => entity.ToModel())
            .Where(model => model != null)    
            .ToList()!;
    }

}