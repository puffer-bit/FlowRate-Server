using Server.Core.Models.Coordinator.Settings;

namespace Server.Core.Models.Coordinator;

/// <summary>Stored configuration of one coordinator.</summary>
public sealed class CoordinatorInstanceModel
{
    public string Id { get; }
    public string Name { get; private set; }
    public bool IsDefault { get; }
    public string Ip { get; private set; }
    public int Port { get; private set; }
    public string? PasswordHash { get; private set; } 
    public DateTime CreateTime { get; }
    public DateTime FirstLaunchTime { get; }
    public DateTime CurrentLaunchTime { get; private set; }
    /// <summary>How many users may be connected at once; <see cref="NoUserLimit"/> for any number.</summary>
    public int MaxOnlineUsers { get; private set; }
    /// <summary>How many users the coordinator may register; <see cref="NoUserLimit"/> for any number.</summary>
    public int MaxUsers { get; private set; }

    public const int NoUserLimit = 0;

    public MotdSettings Motd { get; private set; }
    public AccessListSettings AccessList { get; private set; }
    public CoordinatorHeartbeatSettings Heartbeat { get; private set; }
    public CoordinatorMediaSettings Media { get; private set; }
    public CoordinatorUploadSettings Uploads { get; private set; }
    public CoordinatorRateLimitSettings RateLimits { get; private set; }

    /// <summary>SFU nodes from the orchestrator's global pool available to this coordinator.</summary>
    public IReadOnlyList<int> SfuNodeIds { get; private set; }

    /// <summary>Grows with every saved change, so an editor can tell its copy was changed meanwhile.</summary>
    public long Revision { get; private set; }

    public CoordinatorInstanceModel(string id, string name, bool isDefault, string ip, int port,
        DateTime createTime, DateTime firstLaunchTime, DateTime currentLaunchTime,
        int maxOnlineUsers, int maxUsers,
        MotdSettings motd, AccessListSettings accessList, CoordinatorHeartbeatSettings heartbeat,
        CoordinatorMediaSettings media, CoordinatorUploadSettings uploads, CoordinatorRateLimitSettings rateLimits,
        IEnumerable<int> sfuNodeIds, string? passwordHash = null, long revision = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Id = id;
        Name = ValidateName(name);
        IsDefault = isDefault;
        PasswordHash = passwordHash;
        (Ip, Port) = ValidateEndpoint(ip, port);
        CreateTime = createTime;
        FirstLaunchTime = firstLaunchTime;
        CurrentLaunchTime = currentLaunchTime;
        (MaxOnlineUsers, MaxUsers) = ValidateLimits(maxOnlineUsers, maxUsers);
        Motd = motd;
        AccessList = accessList;
        Heartbeat = heartbeat;
        Media = media;
        Uploads = uploads;
        RateLimits = rateLimits;
        SfuNodeIds = sfuNodeIds.ToList();
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        Revision = revision;
    }

    public static CoordinatorInstanceModel Create(string name, string ip, int port, DateTime now, bool isDefault = false, string? passwordHash = null) =>
        new(Guid.NewGuid().ToString(), name, isDefault, ip, port,now, now, now,
            maxOnlineUsers: 0, maxUsers: 0,
            MotdSettings.Disabled, AccessListSettings.Open, new CoordinatorHeartbeatSettings(0, 0),
            CoordinatorMediaSettings.Default, CoordinatorUploadSettings.Default, CoordinatorRateLimitSettings.Default,
            [], passwordHash);

    public void Rename(string name) => Name = ValidateName(name);
    
    public void SetPassword(string? passwordHash) => PasswordHash = passwordHash;

    public void Listen(string ip, int port) => (Ip, Port) = ValidateEndpoint(ip, port);

    public void SetUserLimits(int maxOnlineUsers, int maxUsers) =>
        (MaxOnlineUsers, MaxUsers) = ValidateLimits(maxOnlineUsers, maxUsers);

    public void SetMotd(MotdSettings motd) => Motd = motd;

    public void SetAccessList(AccessListSettings accessList) => AccessList = accessList;

    public void SetHeartbeat(CoordinatorHeartbeatSettings heartbeat) => Heartbeat = heartbeat;

    public void SetMedia(CoordinatorMediaSettings media) => Media = media;

    public void SetUploads(CoordinatorUploadSettings uploads) => Uploads = uploads;

    public void SetRateLimits(CoordinatorRateLimitSettings rateLimits) => RateLimits = rateLimits;

    public void AssignSfuNodes(IEnumerable<int> sfuNodeIds) => SfuNodeIds = sfuNodeIds.Distinct().ToList();

    public void MarkLaunched(DateTime now) => CurrentLaunchTime = now;

    public void NextRevision() => Revision++;

    private static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }

    private static (string, int) ValidateEndpoint(string ip, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ip);
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, ushort.MaxValue);
        return (ip, port);
    }

    private static (int, int) ValidateLimits(int maxOnlineUsers, int maxUsers)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxOnlineUsers);
        ArgumentOutOfRangeException.ThrowIfNegative(maxUsers);
        return (maxOnlineUsers, maxUsers);
    }
}
