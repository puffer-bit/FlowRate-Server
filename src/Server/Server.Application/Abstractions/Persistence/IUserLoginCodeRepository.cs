namespace Server.Application.Abstractions.Persistence;

/// <summary>One-time login codes the control panel gives out; only hashes are kept.</summary>
public interface IUserLoginCodeRepository
{
    Task AddAsync(string coordinatorInstanceId, int userId, byte[] codeHash, DateTimeOffset createdAt,
        DateTimeOffset expiresAt, CancellationToken cancellationToken = default);

    /// <summary>The user of a code that has not expired, which is used up by it; null for an unknown code.</summary>
    Task<int?> TakeAsync(string coordinatorInstanceId, byte[] codeHash, DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Drops the codes of the user that are still unused.</summary>
    Task DeleteByUserAsync(string coordinatorInstanceId, int userId, CancellationToken cancellationToken = default);
}
