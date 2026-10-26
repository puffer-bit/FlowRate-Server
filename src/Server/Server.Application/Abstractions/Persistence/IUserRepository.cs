using Server.Core.Models.User;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Sessions.Hybrid;

namespace Server.Application.Abstractions.Persistence;

public interface IUserRepository
{
    Task<UserModel?> GetByIdAsync(int userId, CancellationToken cancellationToken = default);
    Task<UserModel?> GetByIdentityAsync(string coordinatorInstanceId, string id,
        CancellationToken cancellationToken = default);
    Task<IDictionary<int, UserModel>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlySet<string>> GetUsernamesStartingWithAsync(string prefix, CancellationToken cancellationToken = default);
    Task<bool> IsUsernameTakenAsync(string username, int exceptUserId, CancellationToken cancellationToken = default);
    Task<int> CountAsync(string coordinatorInstanceId, CancellationToken cancellationToken = default);
    Task<int> AddAsync(UserModel model, string userIdentity, CancellationToken cancellationToken = default);
    Task UpdateAsync(UserModel model, CancellationToken cancellationToken = default);
    Task RemoveAsync(int userId, CancellationToken cancellationToken = default);
    
    // New: return stored UserIdentity for given user id (or null if not found)
    Task<string?> GetIdentityByIdAsync(int userId, CancellationToken cancellationToken = default);
}