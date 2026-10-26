using System.Collections.Concurrent;
using System.Reflection;
using Server.Core.Models.Orchestrator;
using Microsoft.Extensions.Options;
using Server.Application.Options;

namespace Server.Application.Orchestrator
{
    public class OrchestratorInstanceContext : IOrchestratorInstanceContext
    {
        // Main properties
        public required int Id { get; init; } = 1;
        public required string ServerVersion { get; set; }
        public string? Key { get; set; }
        public string Ip { get; set; }
        public string? DnsName { get; set; }
        public int Port { get; set; }
        public int MaxCoordinators { get; set; } = OrchestratorInstanceModel.DefaultMaxCoordinators;
        public int MaxOnlineUsers { get; set; }
        public int MaxUsers { get; set; }
        public SfuConnectionSettings SfuConnection { get; set; } = new();

        // Time properties
        public required string TimeZoneId { get; set; }
        public DateTime FirstLaunchTime { get; set; }
        public DateTime CurrentLaunchTime { get; set; }

        // Hardware and Software parameters
        public required string OperatingSystem { get; set; }
        public required string CpuName { get; set; }
        public required string DbProvider { get; set; }
        public string? GpuName { get; set; }
        public int RamSizeMb { get; set; }
        public int MaxDbSizeMb { get; set; }

        // Region parameters
        public required string RegionName { get; set; }
        public required string RegionCode { get; set; }
        public string? CityName { get; set; }
        public string? Locale { get; set; }

        // Security parameters
        public ConcurrentBag<string> AllowedIPs { get; init; } = new();
        public ConcurrentBag<string> BannedIPs { get; init; } = new();

        public OrchestratorInstanceContext(IOptions<OrchestratorOptions> options)
        {
            // The orchestrator's own address always comes from the configuration file, it is needed to reach the panel
            Ip = options.Value.ListenIp;
            Port = options.Value.Port;
        }

        public void CreateContext()
        {
            ServerVersion = ReadServerVersion();
            
            FirstLaunchTime = DateTime.UtcNow;
            CurrentLaunchTime = FirstLaunchTime;
        }
        
        public void RecreateContext()
        {
            ServerVersion = ReadServerVersion();
            
            CurrentLaunchTime = DateTime.UtcNow;
        }

        // The version is set in the host project, so it is read from the entry assembly, without the commit hash
        private static string ReadServerVersion()
        {
            var version = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return string.IsNullOrEmpty(version) ? "Unknown" : version.Split('+')[0];
        }

        public void LoadContext(OrchestratorInstanceModel orchestratorInstanceEntity)
        {
            throw new NotImplementedException();
        }

        public void UpdateContext(OrchestratorInstanceModel orchestratorInstanceEntity)
        {
            RecreateContext();
            FirstLaunchTime = orchestratorInstanceEntity.FirstLaunchTime;
            MaxCoordinators = orchestratorInstanceEntity.MaxCoordinators;
            SfuConnection = orchestratorInstanceEntity.SfuConnection;
        }

        public OrchestratorInstanceModel AsModel()
        {
            return new OrchestratorInstanceModel()
            {
                ServerVersion = this.ServerVersion,
                Ip = this.Ip,
                Port = this.Port,
                MaxCoordinators = this.MaxCoordinators,
                SfuConnection = this.SfuConnection,
                FirstLaunchTime = this.FirstLaunchTime,
                CurrentLaunchTime = this.CurrentLaunchTime,
                IsFirstLaunch = false
            };
        }
    }
}
