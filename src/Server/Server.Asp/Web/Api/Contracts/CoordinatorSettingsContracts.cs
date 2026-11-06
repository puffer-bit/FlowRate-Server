using Shared.Enums;
using System.ComponentModel.DataAnnotations;

namespace Server.Asp.Web.Api.Contracts;

/// <summary>Stored settings of a coordinator. The server password is never returned, only whether there is one.</summary>
/// <param name="Revision">Grows with every saved change; send it back with the change made from these settings.</param>
public sealed record CoordinatorSettingsDto(
    string Id,
    long Revision,
    string Name,
    bool IsDefault,
    string Ip,
    int Port,
    bool IsPasswordProtected,
    int MaxOnlineUsers,
    int MaxUsers,
    MotdSettingsDto Motd,
    AccessListSettingsDto AccessList,
    HeartbeatSettingsDto Heartbeat,
    MediaSettingsDto Media,
    UploadSettingsDto Uploads,
    RateLimitSettingsDto RateLimits,
    IReadOnlyList<int> SfuNodeIds);

/// <summary>A node of the global SFU pool a coordinator can be given. Credentials are left out.</summary>
/// <param name="Status">How the orchestrator's connection to the node is doing; null for a disabled node.</param>
public sealed record SfuNodeDto(int Id, string Name, string Host, int Port, bool IsSecured, bool IsEnabled,
    SfuNodeStatusDto? Status);

/// <param name="PingMs">Round trip of the last answered ping; null while the node does not answer.</param>
/// <param name="Error">Why the node is not connected, if known.</param>
public sealed record SfuNodeStatusDto(
    SFUNodeConnectionState State,
    double? PingMs,
    DateTimeOffset? LastPingAtUtc,
    DateTimeOffset StateChangedAtUtc,
    string? Error,
    int? SessionCount,
    int? WorkerCount);

/// <summary>
/// The whole new settings of a coordinator. A changed address or port restarts it, which disconnects its users;
/// everything else is applied while it runs. The values are checked by the server, the same way for the panel
/// and for the clients, and refused ones come back as validation problems by field.
/// </summary>
public sealed class UpdateCoordinatorSettingsDto
{
    /// <summary>
    /// The revision the change was made from. When the settings changed since, nothing is saved and the answer
    /// is 409 Conflict.
    /// </summary>
    public long ExpectedRevision { get; init; }

    [Required]
    public string Name { get; init; } = string.Empty;

    [Required]
    public string Ip { get; init; } = string.Empty;

    public int Port { get; init; }

    /// <summary>Sets a new server password. Null keeps the current one.</summary>
    public string? NewPassword { get; init; }

    /// <summary>Lets users in without a server password.</summary>
    public bool RemovePassword { get; init; }

    public int MaxOnlineUsers { get; init; }

    public int MaxUsers { get; init; }

    [Required]
    public MotdSettingsDto Motd { get; init; } = new();

    [Required]
    public AccessListSettingsDto AccessList { get; init; } = new();

    [Required]
    public HeartbeatSettingsDto Heartbeat { get; init; } = new();

    [Required]
    public MediaSettingsDto Media { get; init; } = new();

    [Required]
    public UploadSettingsDto Uploads { get; init; } = new();

    [Required]
    public RateLimitSettingsDto RateLimits { get; init; } = new();

    [Required]
    public IReadOnlyList<int> SfuNodeIds { get; init; } = [];
}

public sealed class MotdSettingsDto
{
    public bool IsEnabled { get; init; }
    public string? Text { get; init; }
}

public sealed class AccessListSettingsDto
{
    /// <summary>When not empty, only these IPs may connect.</summary>
    public IReadOnlyList<string> AllowedIps { get; init; } = [];
    public IReadOnlyList<string> BannedIps { get; init; } = [];
}

public sealed class HeartbeatSettingsDto
{
    public int IntervalSeconds { get; init; }
    public int TimeoutSeconds { get; init; }
}

public sealed class MediaSettingsDto
{
    public long MaxFileSizeBytes { get; init; }

    /// <summary>0 leaves the storage unlimited.</summary>
    public long StorageQuotaBytes { get; init; }
}

/// <summary>Uploads not attached to a message yet, and the tokens media requests carry.</summary>
public sealed class UploadSettingsDto
{
    /// <summary>How many uploads one user may keep before attaching them to a message.</summary>
    public int MaxUnattachedPerUser { get; init; }

    /// <summary>How long an upload not attached to a message is kept.</summary>
    public int UnattachedLifetimeHours { get; init; }

    /// <summary>How long a token for media requests stays valid.</summary>
    public int TokenLifetimeMinutes { get; init; }
}

/// <summary>At most <see cref="Count"/> requests per <see cref="PeriodSeconds"/>.</summary>
public sealed class RequestRateDto
{
    public int Count { get; init; }
    public double PeriodSeconds { get; init; }
}

/// <summary>
/// How many requests and connections the coordinator takes from one client. Every request falls into one group
/// and also counts against <see cref="Global"/>.
/// </summary>
public sealed class RateLimitSettingsDto
{
    /// <summary>Every request of one connection.</summary>
    [Required] public RequestRateDto Global { get; init; } = new();

    /// <summary>Logins from one IP address, over all its connections.</summary>
    [Required] public RequestRateDto AuthenticationPerIp { get; init; } = new();

    [Required] public RequestRateDto SessionManagement { get; init; } = new();
    [Required] public RequestRateDto SessionLayout { get; init; } = new();
    [Required] public RequestRateDto SessionMedia { get; init; } = new();
    [Required] public RequestRateDto MicrophoneMute { get; init; } = new();
    [Required] public RequestRateDto InboundResume { get; init; } = new();
    [Required] public RequestRateDto Moderation { get; init; } = new();
    [Required] public RequestRateDto ChatWrite { get; init; } = new();
    [Required] public RequestRateDto ChatRead { get; init; } = new();
    [Required] public RequestRateDto PushRegistration { get; init; } = new();
    [Required] public RequestRateDto MediaToken { get; init; } = new();

    /// <summary>Every request of no other group.</summary>
    [Required] public RequestRateDto Query { get; init; } = new();

    [Required] public RequestRateDto Heartbeat { get; init; } = new();

    /// <summary>A connection that has this many requests refused within the window is closed.</summary>
    public int MaxRejectionsPerWindow { get; init; }
    public int RejectionWindowSeconds { get; init; }

    /// <summary>How many connections one IP address may keep open.</summary>
    public int MaxConnectionsPerIp { get; init; }
}
