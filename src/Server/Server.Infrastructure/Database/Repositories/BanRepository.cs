using Microsoft.EntityFrameworkCore;
using Server.Application.Abstractions.Persistence;
using Server.Core.Models.Moderation;
using Server.Infrastructure.Database.Entities;

namespace Server.Infrastructure.Database.Repositories;

public class BanRepository(IDbContextFactory<ServerDbContext> factory) : IBanRepository
{
    public async Task<List<GlobalBan>> GetAsync(string coordinatorId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await db.GlobalBans.AsNoTracking().Where(x => x.CoordinatorId == coordinatorId)
            .OrderByDescending(x => x.CreatedAt).ToListAsync(ct)).Select(x => new GlobalBan(x.Id, x.CoordinatorId,
            x.Kind, x.UserId, x.Address, x.Reason, x.CreatedBy, x.CreatedAt, x.ExpiresAt, x.PreviousState,
            x.RevokedBy, x.RevokedAt)).ToList();
    }

    public async Task AddAsync(GlobalBan ban, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.GlobalBans.Add(new BanEntity { Id = ban.Id, CoordinatorId = ban.CoordinatorId, Kind = ban.Kind,
            UserId = ban.UserId, Address = ban.Address, Reason = ban.Reason, CreatedBy = ban.CreatedBy,
            CreatedAt = ban.CreatedAt, ExpiresAt = ban.ExpiresAt, PreviousState = ban.PreviousState });
        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeAsync(string coordinatorId, Guid id, int actorId, DateTime now, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.GlobalBans.Where(x => x.CoordinatorId == coordinatorId && x.Id == id && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedBy, actorId), ct);
    }
}
