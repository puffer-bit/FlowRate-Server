using System.Collections.Concurrent;
using System.Security.Cryptography;
using Shared.Models.DataTransferObjects;

namespace Server.Application.Coordinator.Authentication;

/// <summary>
/// New devices waiting, logged out, for a device of an account to add them. Each shows a short code, good for
/// <see cref="Lifetime"/>; one waits per connection.
/// </summary>
public sealed class DeviceLinks(TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    // No 0/O, 1/I/L: the code is read off a screen and typed
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 8;

    private readonly ConcurrentDictionary<string, PendingDeviceLink> _links = new();

    public DeviceLinks() : this(TimeProvider.System)
    {
    }

    public PendingDeviceLink Start(string connectionId, NewDevice device, DeviceInfoDTO? info, string? address)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var (code, waiting) in _links)
        {
            if (waiting.ExpiresAt <= now || waiting.ConnectionId == connectionId)
                _links.TryRemove(code, out _);
        }

        while (true)
        {
            var link = new PendingDeviceLink(NewCode(), connectionId, device, info, address, now + Lifetime);
            if (_links.TryAdd(link.Code, link))
                return link;
        }
    }

    /// <summary>The link waiting with the code, as typed or scanned; null when there is none or it expired.</summary>
    public PendingDeviceLink? Find(string code)
    {
        var normalized = Normalize(code);
        if (!_links.TryGetValue(normalized, out var link))
            return null;
        if (link.ExpiresAt > timeProvider.GetUtcNow())
            return link;

        _links.TryRemove(normalized, out _);
        return null;
    }

    /// <summary>Takes the link off the list, so it is approved or declined once.</summary>
    public bool Take(PendingDeviceLink link) => _links.TryRemove(new KeyValuePair<string, PendingDeviceLink>(link.Code, link));

    public int Count => _links.Count;

    /// <summary>The code as shown: two groups of four.</summary>
    public static string Format(string code) => code.Length == CodeLength ? $"{code[..4]}-{code[4..]}" : code;

    private static string Normalize(string code)
    {
        var text = code.Trim();
        const string prefix = "flowrate-link:";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            text = text[prefix.Length..];
        return new string(text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    }

    private static string NewCode() =>
        string.Create(CodeLength, 0, (chars, _) =>
        {
            for (var i = 0; i < chars.Length; i++)
                chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });
}

/// <param name="Address">The IP address the new device connected from.</param>
public sealed record PendingDeviceLink(string Code, string ConnectionId, NewDevice Device, DeviceInfoDTO? Info,
    string? Address, DateTimeOffset ExpiresAt);
