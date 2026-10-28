using System.Net;
using Shared.Validation;

namespace Server.Application.Orchestrator.Settings;

/// <summary>
/// The rules every editor of coordinator settings is held to. Errors are keyed by the field they belong to,
/// as named in <see cref="CoordinatorSettingsFields"/>.
/// </summary>
public static class CoordinatorSettingsValidator
{
    private const int MaxHeartbeatSeconds = 3600;

    public static IReadOnlyDictionary<string, string[]> Validate(CoordinatorSettingsChange change,
        IReadOnlySet<int> knownSfuNodeIds)
    {
        var errors = new Errors();

        if (string.IsNullOrWhiteSpace(change.Name))
            errors.Add(CoordinatorSettingsFields.Name, "The name cannot be empty.");
        else if (change.Name.Trim().Length > Limits.CoordinatorNameMaxLength)
            errors.Add(CoordinatorSettingsFields.Name,
                $"The name cannot be longer than {Limits.CoordinatorNameMaxLength} characters.");

        if (!IPAddress.TryParse(change.Ip, out _))
            errors.Add(CoordinatorSettingsFields.Ip, $"'{change.Ip}' is not a valid IP address.");
        ValidatePort(errors, CoordinatorSettingsFields.Port, change.Port);

        if (change.RemovePassword && change.NewPassword != null)
            errors.Add(CoordinatorSettingsFields.NewPassword, "Either set a new password or remove it, not both.");
        else if (change.NewPassword is { Length: 0 })
            errors.Add(CoordinatorSettingsFields.NewPassword, "The password cannot be empty; remove it instead.");
        else if (change.NewPassword?.Length > Limits.PasswordMaxLength)
            errors.Add(CoordinatorSettingsFields.NewPassword,
                $"The password cannot be longer than {Limits.PasswordMaxLength} characters.");

        if (change.MaxOnlineUsers < 1)
            errors.Add(CoordinatorSettingsFields.MaxOnlineUsers, "At least one user must be let online.");
        if (change.MaxUsers < 1)
            errors.Add(CoordinatorSettingsFields.MaxUsers, "At least one user must be let register.");
        else if (change.MaxOnlineUsers > change.MaxUsers)
            errors.Add(CoordinatorSettingsFields.MaxOnlineUsers,
                "More users cannot be online than may be registered.");

        if (change.MotdText?.Length > Limits.MOTDTextMaxLength)
            errors.Add(CoordinatorSettingsFields.MotdText,
                $"The message cannot be longer than {Limits.MOTDTextMaxLength} characters.");

        ValidateIps(errors, CoordinatorSettingsFields.AllowedIps, change.AllowedIps);
        ValidateIps(errors, CoordinatorSettingsFields.BannedIps, change.BannedIps);

        if (change.HeartbeatIntervalSeconds is < 1 or > MaxHeartbeatSeconds)
            errors.Add(CoordinatorSettingsFields.HeartbeatInterval,
                $"The interval must be from 1 to {MaxHeartbeatSeconds} seconds.");
        if (change.HeartbeatTimeoutSeconds is < 1 or > MaxHeartbeatSeconds)
            errors.Add(CoordinatorSettingsFields.HeartbeatTimeout,
                $"The timeout must be from 1 to {MaxHeartbeatSeconds} seconds.");
        else if (change.HeartbeatTimeoutSeconds <= change.HeartbeatIntervalSeconds)
            errors.Add(CoordinatorSettingsFields.HeartbeatTimeout, "The timeout must be longer than the interval.");

        if (change.MaxFileSizeBytes < 1)
            errors.Add(CoordinatorSettingsFields.MaxFileSize, "The largest file must be at least one byte.");
        if (change.StorageQuotaBytes < 0)
            errors.Add(CoordinatorSettingsFields.StorageQuota, "The quota cannot be negative; 0 leaves it unlimited.");

        ValidateUploads(errors, change);
        ValidateRateLimits(errors, change.RateLimits);

        foreach (var unknownId in change.SfuNodeIds.Where(id => !knownSfuNodeIds.Contains(id)).Distinct())
            errors.Add(CoordinatorSettingsFields.SfuNodeIds, $"There is no SFU node {unknownId}.");

        return errors.ToDictionary();
    }

    private static void ValidateUploads(Errors errors, CoordinatorSettingsChange change)
    {
        if (change.MaxUnattachedUploadsPerUser is < 1 or > Limits.UnattachedUploadsMaxCount)
            errors.Add(CoordinatorSettingsFields.MaxUnattachedUploads,
                $"The number must be from 1 to {Limits.UnattachedUploadsMaxCount}.");
        if (change.UnattachedUploadLifetimeHours is < 1 or > Limits.UnattachedUploadLifetimeMaxHours)
            errors.Add(CoordinatorSettingsFields.UnattachedUploadLifetime,
                $"The lifetime must be from 1 to {Limits.UnattachedUploadLifetimeMaxHours} hours.");
        if (change.MediaTokenLifetimeMinutes is < 1 or > Limits.MediaTokenLifetimeMaxMinutes)
            errors.Add(CoordinatorSettingsFields.MediaTokenLifetime,
                $"The lifetime must be from 1 to {Limits.MediaTokenLifetimeMaxMinutes} minutes.");
    }

    private static void ValidateRateLimits(Errors errors, RateLimitsChange limits)
    {
        foreach (var (name, rate) in limits.Rates)
        {
            var field = CoordinatorSettingsFields.RateLimit(name);
            if (rate.Count is < 1 or > Limits.RequestRateCountMax)
                errors.Add($"{field}.Count", $"The count must be from 1 to {Limits.RequestRateCountMax}.");
            if (!(rate.PeriodSeconds >= Limits.RequestRatePeriodMinSeconds
                  && rate.PeriodSeconds <= Limits.RequestRatePeriodMaxSeconds))
                errors.Add($"{field}.PeriodSeconds",
                    $"The period must be from {Limits.RequestRatePeriodMinSeconds} to {Limits.RequestRatePeriodMaxSeconds} seconds.");
        }

        if (limits.MaxRejectionsPerWindow is < 1 or > Limits.RequestRejectionsMax)
            errors.Add(CoordinatorSettingsFields.RateLimit(nameof(limits.MaxRejectionsPerWindow)),
                $"The number must be from 1 to {Limits.RequestRejectionsMax}.");
        if (limits.RejectionWindowSeconds is < 1 or > Limits.RequestRejectionWindowMaxSeconds)
            errors.Add(CoordinatorSettingsFields.RateLimit(nameof(limits.RejectionWindowSeconds)),
                $"The window must be from 1 to {Limits.RequestRejectionWindowMaxSeconds} seconds.");
        if (limits.MaxConnectionsPerIp is < 1 or > Limits.ConnectionsPerIpMax)
            errors.Add(CoordinatorSettingsFields.RateLimit(nameof(limits.MaxConnectionsPerIp)),
                $"The number must be from 1 to {Limits.ConnectionsPerIpMax}.");
    }

    private static void ValidatePort(Errors errors, string field, int port)
    {
        if (port is < 1 or > ushort.MaxValue)
            errors.Add(field, $"The port must be from 1 to {ushort.MaxValue}.");
    }

    private static void ValidateIps(Errors errors, string field, IReadOnlyList<string> ips)
    {
        if (ips.Count > Limits.IpListMaxCount)
            errors.Add(field, $"The list cannot hold more than {Limits.IpListMaxCount} addresses.");

        foreach (var ip in ips.Where(ip => !IPAddress.TryParse(ip, out _)))
            errors.Add(field, $"'{ip}' is not a valid IP address.");
    }

    private sealed class Errors
    {
        private readonly Dictionary<string, List<string>> _errors = new();

        public void Add(string field, string message)
        {
            if (!_errors.TryGetValue(field, out var messages))
                _errors[field] = messages = [];
            messages.Add(message);
        }

        public IReadOnlyDictionary<string, string[]> ToDictionary() =>
            _errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }
}

/// <summary>
/// Names of the fields validation errors are reported for; the panel puts each message next to its field.
/// </summary>
public static class CoordinatorSettingsFields
{
    public const string Name = "Name";
    public const string Ip = "Ip";
    public const string Port = "Port";
    public const string NewPassword = "NewPassword";
    public const string MaxOnlineUsers = "MaxOnlineUsers";
    public const string MaxUsers = "MaxUsers";
    public const string MotdText = "Motd.Text";
    public const string AllowedIps = "AccessList.AllowedIps";
    public const string BannedIps = "AccessList.BannedIps";
    public const string HeartbeatInterval = "Heartbeat.IntervalSeconds";
    public const string HeartbeatTimeout = "Heartbeat.TimeoutSeconds";
    public const string MaxFileSize = "Media.MaxFileSizeBytes";
    public const string StorageQuota = "Media.StorageQuotaBytes";
    public const string MaxUnattachedUploads = "Uploads.MaxUnattachedPerUser";
    public const string UnattachedUploadLifetime = "Uploads.UnattachedLifetimeHours";
    public const string MediaTokenLifetime = "Uploads.TokenLifetimeMinutes";
    public const string SfuNodeIds = "SfuNodeIds";

    /// <summary>A field of the rate limits, as "RateLimits.ChatWrite"; a rate adds ".Count" or ".PeriodSeconds".</summary>
    public static string RateLimit(string name) => $"RateLimits.{name}";
}
