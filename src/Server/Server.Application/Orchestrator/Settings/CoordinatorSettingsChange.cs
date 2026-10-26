using Server.Core.Models.Coordinator.Settings;

namespace Server.Application.Orchestrator.Settings;

/// <summary>
/// The whole new settings of a coordinator as an editor sent them, not checked yet.
/// See <see cref="CoordinatorSettingsValidator"/> for what is accepted.
/// </summary>
public sealed record CoordinatorSettingsChange
{
    public required string Name { get; init; }
    public required string Ip { get; init; }
    public int Port { get; init; }

    /// <summary>Sets a new server password. Null keeps the current one.</summary>
    public string? NewPassword { get; init; }

    /// <summary>Lets users in without a server password.</summary>
    public bool RemovePassword { get; init; }

    public int MaxOnlineUsers { get; init; }
    public int MaxUsers { get; init; }

    public bool IsMotdEnabled { get; init; }
    public string? MotdText { get; init; }

    /// <summary>When not empty, only these IPs may connect.</summary>
    public IReadOnlyList<string> AllowedIps { get; init; } = [];
    public IReadOnlyList<string> BannedIps { get; init; } = [];

    public int HeartbeatIntervalSeconds { get; init; }
    public int HeartbeatTimeoutSeconds { get; init; }

    public long MaxFileSizeBytes { get; init; }

    /// <summary>0 leaves the storage unlimited.</summary>
    public long StorageQuotaBytes { get; init; }

    public int MaxUnattachedUploadsPerUser { get; init; }
    public int UnattachedUploadLifetimeHours { get; init; }
    public int MediaTokenLifetimeMinutes { get; init; }

    public required RateLimitsChange RateLimits { get; init; }

    public IReadOnlyList<int> SfuNodeIds { get; init; } = [];
}

public sealed record RequestRateChange(int Count, double PeriodSeconds);

/// <summary>Raw values of <see cref="CoordinatorRateLimitSettings"/>, not checked yet.</summary>
public sealed record RateLimitsChange
{
    public required RequestRateChange Global { get; init; }
    public required RequestRateChange AuthenticationPerIp { get; init; }
    public required RequestRateChange SessionManagement { get; init; }
    public required RequestRateChange SessionLayout { get; init; }
    public required RequestRateChange SessionMedia { get; init; }
    public required RequestRateChange MicrophoneMute { get; init; }
    public required RequestRateChange InboundResume { get; init; }
    public required RequestRateChange Moderation { get; init; }
    public required RequestRateChange ChatWrite { get; init; }
    public required RequestRateChange ChatRead { get; init; }
    public required RequestRateChange PushRegistration { get; init; }
    public required RequestRateChange MediaToken { get; init; }
    public required RequestRateChange Query { get; init; }
    public required RequestRateChange Heartbeat { get; init; }

    public int MaxRejectionsPerWindow { get; init; }
    public int RejectionWindowSeconds { get; init; }
    public int MaxConnectionsPerIp { get; init; }

    /// <summary>The rates by the field their errors are reported for.</summary>
    public IEnumerable<(string Field, RequestRateChange Rate)> Rates =>
    [
        (nameof(Global), Global), (nameof(AuthenticationPerIp), AuthenticationPerIp),
        (nameof(SessionManagement), SessionManagement), (nameof(SessionLayout), SessionLayout),
        (nameof(SessionMedia), SessionMedia), (nameof(MicrophoneMute), MicrophoneMute),
        (nameof(InboundResume), InboundResume), (nameof(Moderation), Moderation),
        (nameof(ChatWrite), ChatWrite), (nameof(ChatRead), ChatRead),
        (nameof(PushRegistration), PushRegistration), (nameof(MediaToken), MediaToken),
        (nameof(Query), Query), (nameof(Heartbeat), Heartbeat)
    ];

    public static RateLimitsChange From(CoordinatorRateLimitSettings settings) => new()
    {
        Global = From(settings.Global),
        AuthenticationPerIp = From(settings.AuthenticationPerIp),
        SessionManagement = From(settings.SessionManagement),
        SessionLayout = From(settings.SessionLayout),
        SessionMedia = From(settings.SessionMedia),
        MicrophoneMute = From(settings.MicrophoneMute),
        InboundResume = From(settings.InboundResume),
        Moderation = From(settings.Moderation),
        ChatWrite = From(settings.ChatWrite),
        ChatRead = From(settings.ChatRead),
        PushRegistration = From(settings.PushRegistration),
        MediaToken = From(settings.MediaToken),
        Query = From(settings.Query),
        Heartbeat = From(settings.Heartbeat),
        MaxRejectionsPerWindow = settings.MaxRejectionsPerWindow,
        RejectionWindowSeconds = settings.RejectionWindowSeconds,
        MaxConnectionsPerIp = settings.MaxConnectionsPerIp
    };

    /// <summary>The settings these values make; call only once they passed validation.</summary>
    public CoordinatorRateLimitSettings ToSettings() => new()
    {
        Global = ToSetting(Global),
        AuthenticationPerIp = ToSetting(AuthenticationPerIp),
        SessionManagement = ToSetting(SessionManagement),
        SessionLayout = ToSetting(SessionLayout),
        SessionMedia = ToSetting(SessionMedia),
        MicrophoneMute = ToSetting(MicrophoneMute),
        InboundResume = ToSetting(InboundResume),
        Moderation = ToSetting(Moderation),
        ChatWrite = ToSetting(ChatWrite),
        ChatRead = ToSetting(ChatRead),
        PushRegistration = ToSetting(PushRegistration),
        MediaToken = ToSetting(MediaToken),
        Query = ToSetting(Query),
        Heartbeat = ToSetting(Heartbeat),
        MaxRejectionsPerWindow = MaxRejectionsPerWindow,
        RejectionWindowSeconds = RejectionWindowSeconds,
        MaxConnectionsPerIp = MaxConnectionsPerIp
    };

    private static RequestRateChange From(RequestRateSetting setting) => new(setting.Count, setting.PeriodSeconds);

    private static RequestRateSetting ToSetting(RequestRateChange rate) => new(rate.Count, rate.PeriodSeconds);
}
