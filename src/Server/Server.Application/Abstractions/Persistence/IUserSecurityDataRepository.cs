using Server.Core.Models.User;

namespace Server.Application.Abstractions.Persistence;

public interface IUserSecurityDataRepository
{
    Task<UserSecurityData?> GetByUserIdAsync(int userId, CancellationToken ct = default);
    Task AddAsync(UserSecurityData model, CancellationToken ct = default);
    Task UpdateAsync(UserSecurityData model, CancellationToken ct = default);
}