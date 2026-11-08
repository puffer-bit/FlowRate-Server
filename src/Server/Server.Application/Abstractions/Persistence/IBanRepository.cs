using Server.Core.Models.Moderation;

namespace Server.Application.Abstractions.Persistence;

public interface IBanRepository
{
    Task<List<GlobalBan>> GetAsync(string coordinatorId, CancellationToken ct = default);
    Task AddAsync(GlobalBan ban, CancellationToken ct = default);
    Task RevokeAsync(string coordinatorId, Guid id, int actorId, DateTime now, CancellationToken ct = default);
}
