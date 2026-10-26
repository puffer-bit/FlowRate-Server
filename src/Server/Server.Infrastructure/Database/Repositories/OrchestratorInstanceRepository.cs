using Microsoft.EntityFrameworkCore;
using Server.Core.Models.Orchestrator;
using Server.Infrastructure.Mappers;
using Server.Application.Abstractions.Persistence;

namespace Server.Infrastructure.Database.Repositories;

public class OrchestratorInstanceRepository : IOrchestratorInstanceRepository
{
    private readonly IDbContextFactory<ServerDbContext> _contextFactory;

    public OrchestratorInstanceRepository(IDbContextFactory<ServerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<OrchestratorInstanceModel?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.OrchestratorInstance
            .FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);
        
        return entity?.ToModel();
    }
    
    public async Task SaveOrUpdateAsync(OrchestratorInstanceModel model, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await context.OrchestratorInstance
            .FirstOrDefaultAsync(x => x.Id == model.Id, cancellationToken);

        if (existing == null)
        {
            await context.OrchestratorInstance.AddAsync(model.ToEntity(), cancellationToken);
        }
        else
        {
            context.Entry(existing).CurrentValues.SetValues(model.ToEntity());
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}