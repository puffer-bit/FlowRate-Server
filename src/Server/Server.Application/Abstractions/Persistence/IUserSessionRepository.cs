using Server.Core.Models.User;

namespace Server.Application.Abstractions.Persistence;

public interface IUserSessionRepository
{
    Task AddAsync(UserSession session, CancellationToken cancellationToken = default);
    Task UpdateAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>The session the token, or the one before it, belongs to; expired sessions are not found.</summary>
    Task<UserSession?> FindByTokenAsync(string coordinatorInstanceId, byte[] tokenHash, DateTime now,
        CancellationToken cancellationToken = default);

    /// <summary>The sessions of the user that have not expired, the latest used first.</summary>
    Task<IReadOnlyList<UserSession>> GetByUserAsync(string coordinatorInstanceId, int userId, DateTime now,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes the session when it is the user's; false when there was none.</summary>
    Task<bool> DeleteAsync(string coordinatorInstanceId, int userId, Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes every session of the user but <paramref name="keep"/>, and the user's expired ones.</summary>
    Task<IReadOnlyList<Guid>> DeleteAllButAsync(string coordinatorInstanceId, int userId, Guid? keep,
        CancellationToken cancellationToken = default);

    Task DeleteExpiredAsync(DateTime now, CancellationToken cancellationToken = default);
}
