namespace Server.Core.Models.Orchestrator;

/// <summary>Stored state of the orchestrator; there is exactly one.</summary>
public sealed record OrchestratorInstanceModel
{
    public const int SingletonId = 1;

    public int Id => SingletonId;
    public required string ServerVersion { get; init; }
    public string? LicenseKey { get; init; }
    public string? Ip { get; init; }
    public string? DnsName { get; init; }
    public int Port { get; init; }
    public int MaxCoordinators { get; init; } = DefaultMaxCoordinators;
    public int MaxOnlineUsers { get; init; }
    public int MaxUsers { get; init; }

    public string? TimeZoneId { get; init; }
    public DateTime FirstLaunchTime { get; init; }
    public DateTime CurrentLaunchTime { get; init; }

    public SfuConnectionSettings SfuConnection { get; init; } = new();

    public OrchestratorHostInfo Host { get; init; } = new();
    public OrchestratorRegionInfo Region { get; init; } = new();

    public bool IsFirstLaunch { get; init; } = true;

    public const int DefaultMaxCoordinators = 4;
}

/// <summary>gRPC connection settings shared by all SFU nodes.</summary>
public sealed record SfuConnectionSettings
{
    public int ConnectTimeoutSeconds { get; init; } = 5;
    public int KeepAlivePingSeconds { get; init; } = 15;
    public int KeepAliveTimeoutSeconds { get; init; } = 5;

    /// <summary>Deadline of a single SFU call; a call that runs longer is reported as timed out.</summary>
    public int CallTimeoutSeconds { get; init; } = 5;

    /// <summary>How often each node is pinged to measure the round trip and notice a node that stopped answering.</summary>
    public int PingIntervalSeconds { get; init; } = 5;
}

/// <summary>Hardware and software the orchestrator runs on.</summary>
public sealed record OrchestratorHostInfo
{
    public string? OperatingSystem { get; init; }
    public string? CpuName { get; init; }
    public string? GpuName { get; init; }
    public int RamSizeMb { get; init; }
    public string? DbProvider { get; init; }
    public int MaxDbSizeMb { get; init; }
}

public sealed record OrchestratorRegionInfo
{
    public string? RegionName { get; init; }
    public string? RegionCode { get; init; }
    public string? CityName { get; init; }
    public string? Locale { get; init; }
}
