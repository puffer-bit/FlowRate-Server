using Server.Application.Abstractions.Persistence;
using Server.Core.Models.User;

namespace Server.Application.Tests.Fakes;

public sealed class InMemoryUserSessionRepository : IUserSessionRepository
{
    private readonly Dictionary<Guid, UserSession> _sessions = [];

    public IReadOnlyCollection<UserSession> All => _sessions.Values;

    public Task AddAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        _sessions.Add(session.Id, session);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        _sessions[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task<UserSession?> FindByTokenAsync(string coordinatorInstanceId, byte[] tokenHash, DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_sessions.Values.FirstOrDefault(x => x.CoordinatorInstanceId == coordinatorInstanceId
            && x.ExpiresAt > now
            && x.PublicKey == null
            && (x.TokenHash?.SequenceEqual(tokenHash) == true || x.PreviousTokenHash?.SequenceEqual(tokenHash) == true)));

    public Task<UserSession?> GetAsync(string coordinatorInstanceId, Guid deviceId, DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_sessions.TryGetValue(deviceId, out var session)
                        && session.CoordinatorInstanceId == coordinatorInstanceId && session.ExpiresAt > now
            ? session
            : null);

    public Task<bool> IsPublicKeyTakenAsync(byte[] publicKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_sessions.Values.Any(x => x.PublicKey?.SequenceEqual(publicKey) == true));

    public Task<IReadOnlyList<Guid>> DeleteLegacyAsync(string coordinatorInstanceId, int userId,
        CancellationToken cancellationToken = default)
    {
        var ids = _sessions.Values
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId && x.UserId == userId && x.PublicKey == null)
            .Select(x => x.Id)
            .ToList();
        foreach (var id in ids)
            _sessions.Remove(id);
        return Task.FromResult<IReadOnlyList<Guid>>(ids);
    }

    public Task<IReadOnlyList<UserSession>> GetByUserAsync(string coordinatorInstanceId, int userId, DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserSession>>(_sessions.Values
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId && x.UserId == userId && x.ExpiresAt > now)
            .OrderByDescending(x => x.LastUsedAt)
            .ToList());

    public Task<bool> DeleteAsync(string coordinatorInstanceId, int userId, Guid sessionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_sessions.TryGetValue(sessionId, out var session) && session.UserId == userId
            && session.CoordinatorInstanceId == coordinatorInstanceId && _sessions.Remove(sessionId));

    public Task<IReadOnlyList<Guid>> DeleteAllButAsync(string coordinatorInstanceId, int userId, Guid? keep,
        CancellationToken cancellationToken = default)
    {
        var ids = _sessions.Values
            .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId && x.UserId == userId && x.Id != keep)
            .Select(x => x.Id)
            .ToList();
        foreach (var id in ids)
            _sessions.Remove(id);
        return Task.FromResult<IReadOnlyList<Guid>>(ids);
    }

    public Task DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        foreach (var session in _sessions.Values.Where(x => x.ExpiresAt <= now).ToList())
            _sessions.Remove(session.Id);
        return Task.CompletedTask;
    }

    public Task ExtendAsync(string coordinatorInstanceId, IReadOnlyCollection<Guid> deviceIds, DateTimeOffset now,
        DateTimeOffset expiresAt, DateTimeOffset temporaryExpiresAt, CancellationToken cancellationToken = default)
    {
        foreach (var session in _sessions.Values.Where(x => deviceIds.Contains(x.Id)
                     && x.CoordinatorInstanceId == coordinatorInstanceId && x.ExpiresAt > now).ToList())
            _sessions[session.Id] = session with { ExpiresAt = session.IsTemporary ? temporaryExpiresAt : expiresAt };
        return Task.CompletedTask;
    }
}

public sealed class InMemoryUserLoginCodeRepository : IUserLoginCodeRepository
{
    private readonly List<(string Coordinator, int UserId, byte[] Hash, DateTimeOffset ExpiresAt)> _codes = [];

    public Task AddAsync(string coordinatorInstanceId, int userId, byte[] codeHash, DateTimeOffset createdAt,
        DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        _codes.Add((coordinatorInstanceId, userId, codeHash, expiresAt));
        return Task.CompletedTask;
    }

    public Task<int?> TakeAsync(string coordinatorInstanceId, byte[] codeHash, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var index = _codes.FindIndex(x => x.Coordinator == coordinatorInstanceId && x.Hash.SequenceEqual(codeHash));
        if (index < 0)
            return Task.FromResult<int?>(null);

        var code = _codes[index];
        _codes.RemoveAt(index);
        return Task.FromResult<int?>(code.ExpiresAt > now ? code.UserId : null);
    }

    public Task DeleteByUserAsync(string coordinatorInstanceId, int userId, CancellationToken cancellationToken = default)
    {
        _codes.RemoveAll(x => x.Coordinator == coordinatorInstanceId && x.UserId == userId);
        return Task.CompletedTask;
    }
}
