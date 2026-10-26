using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Server.Application.Coordinator.Media;

public sealed class MediaTokenService
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private long _lifetimeTicks;
    private readonly TimeProvider _timeProvider;

    public MediaTokenService(TimeSpan lifetime, TimeProvider? timeProvider = null)
    {
        Lifetime = lifetime;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>How long tokens issued from now on stay valid.</summary>
    public TimeSpan Lifetime
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _lifetimeTicks));
        set => Interlocked.Exchange(ref _lifetimeTicks, value.Ticks);
    }

    public (string Token, DateTime ExpiresAt) Issue(int userId)
    {
        var expiresAt = _timeProvider.GetUtcNow().Add(Lifetime);
        var payload = string.Create(CultureInfo.InvariantCulture, $"{userId}.{expiresAt.ToUnixTimeSeconds()}");
        return ($"{payload}.{Sign(payload)}", expiresAt.UtcDateTime);
    }

    public bool TryValidate(string? token, out int userId)
    {
        userId = 0;
        if (string.IsNullOrEmpty(token) || token.Length > 128)
            return false;

        var parts = token.Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var expires))
            return false;

        var expected = Encoding.ASCII.GetBytes(Sign($"{parts[0]}.{parts[1]}"));
        var actual = Encoding.ASCII.GetBytes(parts[2]);
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            return false;

        if (_timeProvider.GetUtcNow().ToUnixTimeSeconds() >= expires)
            return false;

        userId = id;
        return true;
    }

    private string Sign(string payload) =>
        Base64Url.EncodeToString(HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(payload)));
}
