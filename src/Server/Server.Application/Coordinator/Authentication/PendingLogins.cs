using System.Collections.Concurrent;
using Server.Core.Models.User;
using Shared.Enums.Auth;
using Shared.Models.DataTransferObjects;

namespace Server.Application.Coordinator.Authentication;

/// <summary>
/// Connections that named an account and still have to prove a factor of it (the second stage). Kept by connection,
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

    public void Start(string connectionId, PendingLogin login)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var (id, pending) in _logins)
        {
            if (now - pending.StartedAt > Lifetime)
                _logins.TryRemove(id, out _);
        }

        login.StartedAt = now;
        _logins[connectionId] = login;
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

/// <param name="Method">
/// <see cref="DeviceLoginMethod.Factors"/> for a login by username, <see cref="DeviceLoginMethod.Legacy"/> for an
/// identity of the earlier protocol 3, whose account gets <paramref name="LegacyKeys"/>.
/// </param>
public sealed class PendingLogin(LoginChallenge challenge, NewDevice device, DeviceInfoDTO? info,
    DeviceLoginMethod method, UserRecoveryKeys? legacyKeys = null)
{
    public LoginChallenge Challenge { get; } = challenge;
    public NewDevice Device { get; } = device;
    public DeviceInfoDTO? Info { get; } = info;
    public DeviceLoginMethod Method { get; } = method;
    public UserRecoveryKeys? LegacyKeys { get; } = legacyKeys;
    public DateTimeOffset StartedAt { get; internal set; }
    internal int Failures;
}
