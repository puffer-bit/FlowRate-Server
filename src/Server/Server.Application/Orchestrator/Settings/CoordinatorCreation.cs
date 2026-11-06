using Server.Core.Enums;
using Server.Core.Models.Coordinator;
using Server.Core.Models.Coordinator.Settings;

namespace Server.Application.Orchestrator.Settings;

/// <summary>Initial settings for a new coordinator; other settings use the server defaults.</summary>
public sealed record CreateCoordinator(string Name, string Ip, int Port, IReadOnlyList<int> SfuNodeIds,
    int MaxOnlineUsers = 64, int MaxUsers = 256)
{
    public CoordinatorSettingsChange ToSettingsChange() => new()
    {
        Name = Name.Trim(), Ip = Ip.Trim(), Port = Port,
        MaxOnlineUsers = MaxOnlineUsers, MaxUsers = MaxUsers,
        HeartbeatIntervalSeconds = 5, HeartbeatTimeoutSeconds = 40,
        MaxFileSizeBytes = CoordinatorMediaSettings.Default.MaxFileSizeBytes,
        StorageQuotaBytes = CoordinatorMediaSettings.Default.StorageQuotaBytes,
        MaxUnattachedUploadsPerUser = CoordinatorUploadSettings.Default.MaxUnattachedUploadsPerUser,
        UnattachedUploadLifetimeHours = CoordinatorUploadSettings.Default.UnattachedLifetimeHours,
        MediaTokenLifetimeMinutes = CoordinatorUploadSettings.Default.TokenLifetimeMinutes,
        RateLimits = RateLimitsChange.From(CoordinatorRateLimitSettings.Default),
        SfuNodeIds = SfuNodeIds
    };
}

public enum CoordinatorCreationStatus { Created, Invalid, LimitReached, Unavailable }

/// <summary>Created means saved. State reports whether its listener actually started.</summary>
public sealed record CoordinatorCreationResult(CoordinatorCreationStatus Status,
    CoordinatorInstanceModel? Settings = null, CoordinatorState State = CoordinatorState.Offline,
    IReadOnlyDictionary<string, string[]>? Errors = null);
