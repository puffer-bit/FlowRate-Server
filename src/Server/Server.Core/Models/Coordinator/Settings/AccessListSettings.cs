using Server.Core.Models.Moderation;

namespace Server.Core.Models.Coordinator.Settings;

public sealed record AccessListSettings
{
    public static AccessListSettings Open { get; } = new([]);

    /// <summary>When not empty, only these IPs may connect. Addresses kept out are bans.</summary>
    public IReadOnlyList<string> AllowedIps { get; }

    public AccessListSettings(IEnumerable<string> allowedIps)
    {
        AllowedIps = allowedIps.ToList();
    }

    public bool IsAllowed(string ip) =>
        AllowedIps.Count == 0 || AllowedIps.Any(value => Ban.NormalizeAddress(value) == Ban.NormalizeAddress(ip));
}
