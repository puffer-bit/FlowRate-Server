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
    
    /// <summary>Gives the user a new identity; the old one no longer finds the user.</summary>
    Task SetIdentityAsync(int userId, string userIdentity, CancellationToken cancellationToken = default);
}