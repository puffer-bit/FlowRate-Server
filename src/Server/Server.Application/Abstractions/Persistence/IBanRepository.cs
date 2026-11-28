using Server.Core.Models.Moderation;

namespace Server.Application.Abstractions.Persistence;

public interface IBanRepository
{
    Task<List<Ban>> GetAsync(string coordinatorId, CancellationToken ct = default);
    Task AddAsync(Ban ban, CancellationToken ct = default);
    Task DeleteAsync(string coordinatorId, Guid id, CancellationToken ct = default);
}
