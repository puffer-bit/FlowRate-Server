namespace Server.Core.Models.Coordinator.Settings;

public sealed record AccessListSettings
{
    public static AccessListSettings Open { get; } = new([], []);

    /// <summary>When not empty, only these IPs may connect.</summary>
    public IReadOnlyList<string> AllowedIps { get; }
    public IReadOnlyList<string> BannedIps { get; }

    public AccessListSettings(IEnumerable<string> allowedIps, IEnumerable<string> bannedIps)
    {
        AllowedIps = allowedIps.ToList();
        BannedIps = bannedIps.ToList();
    }

    public bool IsAllowed(string ip) =>
        !BannedIps.Any(value => Server.Core.Models.Moderation.GlobalBan.NormalizeAddress(value) == Server.Core.Models.Moderation.GlobalBan.NormalizeAddress(ip))
        && (AllowedIps.Count == 0 || AllowedIps.Any(value => Server.Core.Models.Moderation.GlobalBan.NormalizeAddress(value) == Server.Core.Models.Moderation.GlobalBan.NormalizeAddress(ip)));
}
