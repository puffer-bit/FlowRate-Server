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

    public Task<UserSession?> FindByTokenAsync(string coordinatorInstanceId, byte[] tokenHash, DateTime now,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_sessions.Values.FirstOrDefault(x => x.CoordinatorInstanceId == coordinatorInstanceId
            && x.ExpiresAt > now
            && (x.TokenHash.SequenceEqual(tokenHash) || x.PreviousTokenHash?.SequenceEqual(tokenHash) == true)));

    public Task<IReadOnlyList<UserSession>> GetByUserAsync(string coordinatorInstanceId, int userId, DateTime now,
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

    public Task DeleteExpiredAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        foreach (var session in _sessions.Values.Where(x => x.ExpiresAt <= now).ToList())
            _sessions.Remove(session.Id);
        return Task.CompletedTask;
    }
}
