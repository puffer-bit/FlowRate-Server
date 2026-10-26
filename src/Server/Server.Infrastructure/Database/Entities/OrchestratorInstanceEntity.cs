namespace Server.Infrastructure.Database.Entities
{
    public class OrchestratorInstanceEntity
    {
        // Main properties
        public int Id { get; private init; } = 1;
        public required string ServerVersion { get; set; }
        public string? LicenseKey { get; set; } // :)
        public string Ip { get; set; }
        public string? DnsName { get; set; }
        public int Port { get; set; }
        public int MaxCoordinators { get; set; }
        public int MaxOnlineUsers { get; set; }
        public int MaxUsers { get; set; }

        // gRPC connection to the SFU nodes
        public int SfuConnectTimeoutSeconds { get; set; }
        public int SfuKeepAlivePingSeconds { get; set; }
        public int SfuKeepAliveTimeoutSeconds { get; set; }
        public int SfuCallTimeoutSeconds { get; set; }
        public int SfuPingIntervalSeconds { get; set; }

        // Time properties
        public string? TimeZoneId { get; set; }
        public DateTime FirstLaunchTime { get; set; }
        public DateTime CurrentLaunchTime { get; set; }

        // Hardware and Software parametrs
        public string? OperatingSystem { get; set; }
        public string? CpuName { get; set; }
        public string? DbProvider { get; set; }
        public string? GpuName { get; set; } 
        public int RamSizeMb { get; set; }
        public int MaxDbSizeMb { get; set; }

        // Region parametrs
        public string? RegionName { get; set; }
        public string? RegionCode { get; set; }
        public string? CityName { get; set; }
        public string? Locale { get; set; }

        // Security parametrs
        public bool IsFirstLaunch { get; set; } = true;
    }
}
