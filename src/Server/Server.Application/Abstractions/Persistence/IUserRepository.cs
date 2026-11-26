using Server.Core.Models.User;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Sessions.Hybrid;

namespace Server.Application.Abstractions.Persistence;

public interface IUserRepository
{
    Task<UserModel?> GetByIdAsync(int userId, CancellationToken cancellationToken = default);
    /// <summary>The user with the identity of the earlier protocol 3, until the account moved over to devices.</summary>
    Task<UserModel?> GetByIdentityAsync(string coordinatorInstanceId, string id,
        CancellationToken cancellationToken = default);

    /// <summary>The user whose recovery key has this hash.</summary>
    Task<UserModel?> GetByRecoveryKeyHashAsync(string coordinatorInstanceId, byte[] recoveryKeyHash,
        CancellationToken cancellationToken = default);

    /// <summary>The user with this name, ignoring case; null when there is none, or more than one.</summary>
    Task<UserModel?> GetByUsernameAsync(string coordinatorInstanceId, string username,
        CancellationToken cancellationToken = default);
    Task<IDictionary<int, UserModel>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlySet<string>> GetUsernamesStartingWithAsync(string prefix, CancellationToken cancellationToken = default);
    Task<bool> IsUsernameTakenAsync(string username, int exceptUserId, CancellationToken cancellationToken = default);
    /// <summary>The people of the coordinator, without bots.</summary>
    Task<int> CountAsync(string coordinatorInstanceId, CancellationToken cancellationToken = default);

    /// <summary>The bots of the coordinator that count in its limit: debug bots do not.</summary>
    Task<int> CountBotsAsync(string coordinatorInstanceId, CancellationToken cancellationToken = default);

    /// <summary>The bots of the coordinator, debug ones too, by id.</summary>
    Task<IReadOnlyList<UserModel>> GetBotsAsync(string coordinatorInstanceId,
        CancellationToken cancellationToken = default);

    /// <summary>The bots made by the user lose their owner; returns their ids.</summary>
    Task<IReadOnlyList<int>> ClearBotOwnerAsync(int ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a new user with the recovery keys of the account; returns its id. A bot has no recovery keys.
    /// </summary>
    Task<int> AddAsync(UserModel model, UserRecoveryKeys? recoveryKeys, CancellationToken cancellationToken = default);
    Task UpdateAsync(UserModel model, CancellationToken cancellationToken = default);
    Task RemoveAsync(int userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gives the user new recovery keys; the old phrase no longer finds the user. Drops the identity of the earlier
    /// protocol 3 too, which the keys replace.
    /// </summary>
    Task SetRecoveryKeysAsync(int userId, UserRecoveryKeys recoveryKeys, CancellationToken cancellationToken = default);

    /// <summary>Whether another account has recovery keys with this hash.</summary>
    Task<bool> IsRecoveryKeyTakenAsync(byte[] recoveryKeyHash, int exceptUserId,
        CancellationToken cancellationToken = default);
}