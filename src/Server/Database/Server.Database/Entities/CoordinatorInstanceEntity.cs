using Server.Core.Models.Coordinator;
using Server.Core.Models.Coordinator.Settings;

namespace Server.Database.Entities
{
    public class CoordinatorInstanceEntity
    {
        public required string Id { get; init; }
        public required string Name { get; set; }
        public string? PasswordHash { get; set; }
        public bool IsDefault { get; set; }
        public required string Ip { get; set; }
        public int Port { get; set; }
        public DateTimeOffset CreateTime { get; set; }
        public DateTimeOffset FirstLaunchTime { get; set; }
        public DateTimeOffset CurrentLaunchTime { get; set; }
        public int MaxOnlineUsers { get; set; }
        public int MaxUsers { get; set; }
        /// <summary>How many bots may be made; 0 lets none be.</summary>
        public int MaxBots { get; set; } = CoordinatorInstanceModel.DefaultMaxBots;
        public long Revision { get; set; }

        // Security parametrs
        public bool IsMOTDEnabled { get; set; }
        public string? MotdText { get; set; }
        public List<string> AllowedIPs { get; set; } = new();
        public List<string> BannedIPs { get; set; } = new();

        // Heartbeat
        public int HeartbeatIntervalSeconds { get; set; }
        public int HeartbeatTimeoutSeconds { get; set; }

        public long MediaMaxFileSizeBytes { get; set; }
        public long MediaStorageQuotaBytes { get; set; }
        public int MediaImageMaxSide { get; set; }
        public int MediaImageQuality { get; set; }
        public bool MediaAllowOriginalImages { get; set; }
        public int MediaVideoMaxSide { get; set; }
        public bool MediaAllowOriginalVideos { get; set; }

        public int MaxUnattachedUploadsPerUser { get; set; }
        public int UnattachedUploadLifetimeHours { get; set; }
        public int MediaTokenLifetimeMinutes { get; set; }

        // Stored as JSON, see CoordinatorInstanceConfiguration
        public required CoordinatorRateLimitSettings RateLimits { get; set; }

        // SFU nodes available to this coordinator (join table CoordinatorSfuNodes)
        public List<SfuNodeEntity> SfuNodes { get; set; } = new();
    }
}
