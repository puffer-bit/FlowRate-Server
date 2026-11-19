using System.Collections.Concurrent;
using Shared.Models.DataTransferObjects;

namespace Server.Application.Coordinator.Authentication;

/// <summary>
/// Connections that gave a known identity and still have to prove it (the second stage). Kept by connection,
/// for a short time and a few tries; a connection that does not finish is closed by the authentication timeout.
/// </summary>
public sealed class PendingLogins(TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    public const int MaxFailures = 3;

    private readonly ConcurrentDictionary<string, PendingLogin> _logins = new();

    public PendingLogins() : this(TimeProvider.System)
    {
    }

    public void Start(string connectionId, string userIdentity, DeviceInfoDTO? device, LoginChallenge challenge)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var (id, login) in _logins)
        {
            if (now - login.StartedAt > Lifetime)
                _logins.TryRemove(id, out _);
        }

        _logins[connectionId] = new PendingLogin(userIdentity, device, challenge, now);
    }

    /// <summary>The login the connection started, unless it is too old.</summary>
    public PendingLogin? Get(string connectionId)
    {
        if (!_logins.TryGetValue(connectionId, out var login))
            return null;
        if (timeProvider.GetUtcNow() - login.StartedAt <= Lifetime)
            return login;

        _logins.TryRemove(connectionId, out _);
        return null;
    }

    /// <summary>Counts a wrong answer; true when the connection has no tries left and its login is dropped.</summary>
    public bool RegisterFailure(string connectionId)
    {
        if (!_logins.TryGetValue(connectionId, out var login))
            return true;
        if (Interlocked.Increment(ref login.Failures) < MaxFailures)
            return false;

        _logins.TryRemove(connectionId, out _);
        return true;
    }

    public void Complete(string connectionId) => _logins.TryRemove(connectionId, out _);

    public int Count => _logins.Count;
}

public sealed class PendingLogin(string userIdentity, DeviceInfoDTO? device, LoginChallenge challenge,
    DateTimeOffset startedAt)
{
    public string UserIdentity { get; } = userIdentity;
    public DeviceInfoDTO? Device { get; } = device;
    public LoginChallenge Challenge { get; } = challenge;
    public DateTimeOffset StartedAt { get; } = startedAt;
    internal int Failures;
}
