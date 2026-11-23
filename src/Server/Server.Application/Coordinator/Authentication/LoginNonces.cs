using System.Collections.Concurrent;

namespace Server.Application.Coordinator.Authentication;

/// <summary>
/// The challenges connections sign to log in with a device or the recovery key. One per connection, good for one login
/// and <see cref="Lifetime"/>.
/// </summary>
public sealed class LoginNonces(TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, (string Nonce, DateTimeOffset IssuedAt)> _nonces = new();

    public LoginNonces() : this(TimeProvider.System)
    {
    }

    public string Issue(string connectionId)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var (id, issued) in _nonces)
        {
            if (now - issued.IssuedAt > Lifetime)
                _nonces.TryRemove(id, out _);
        }

        var nonce = SecretHash.CreateToken();
        _nonces[connectionId] = (nonce, now);
        return nonce;
    }

    /// <summary>The challenge of the connection, used up by it; null when it has none or it is too old.</summary>
    public string? Take(string connectionId)
    {
        if (!_nonces.TryRemove(connectionId, out var issued))
            return null;
        return timeProvider.GetUtcNow() - issued.IssuedAt <= Lifetime ? issued.Nonce : null;
    }
}
