using Server.Core.Models.User;

namespace Server.Application.Abstractions.Persistence;

/// <summary>The devices of users' accounts (named sessions in the database).</summary>
public interface IUserSessionRepository
{
    Task AddAsync(UserSession session, CancellationToken cancellationToken = default);
    Task UpdateAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>The device, unless it has expired.</summary>
    Task<UserSession?> GetAsync(string coordinatorInstanceId, Guid deviceId, DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The session of the earlier protocol 3 the token, or the one before it, belongs to; expired sessions are not found.
    /// </summary>
    Task<UserSession?> FindByTokenAsync(string coordinatorInstanceId, byte[] tokenHash, DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Whether a device of any account has this public key.</summary>
    Task<bool> IsPublicKeyTakenAsync(byte[] publicKey, CancellationToken cancellationToken = default);

    /// <summary>The devices of the user that have not expired, the latest used first.</summary>
    Task<IReadOnlyList<UserSession>> GetByUserAsync(string coordinatorInstanceId, int userId, DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes the device when it is the user's; false when there was none.</summary>
    Task<bool> DeleteAsync(string coordinatorInstanceId, int userId, Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes every device of the user but <paramref name="keep"/>, and the user's expired ones.</summary>
    Task<IReadOnlyList<Guid>> DeleteAllButAsync(string coordinatorInstanceId, int userId, Guid? keep,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes the sessions of the earlier protocol 3 the user has, once the account moved over to devices.</summary>
    Task<IReadOnlyList<Guid>> DeleteLegacyAsync(string coordinatorInstanceId, int userId,
        CancellationToken cancellationToken = default);

    Task DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the expiry of those of the devices that have not expired yet to <paramref name="expiresAt"/>, or to
    /// <paramref name="temporaryExpiresAt"/> for temporary ones. Their last login stays as it is.
    /// </summary>
    Task ExtendAsync(string coordinatorInstanceId, IReadOnlyCollection<Guid> deviceIds, DateTimeOffset now,
        DateTimeOffset expiresAt, DateTimeOffset temporaryExpiresAt, CancellationToken cancellationToken = default);
}
