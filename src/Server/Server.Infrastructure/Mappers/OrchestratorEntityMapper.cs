using Server.Core.Models.Orchestrator;
using Server.Infrastructure.Database.Entities;

namespace Server.Infrastructure.Mappers;

public static class OrchestratorEntityMapper
{
    public static OrchestratorInstanceModel ToModel(this OrchestratorInstanceEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return new OrchestratorInstanceModel
        {
            ServerVersion = entity.ServerVersion,
            LicenseKey = entity.LicenseKey,
            Ip = entity.Ip,
            DnsName = entity.DnsName,
            Port = entity.Port,
            MaxCoordinators = entity.MaxCoordinators,
            MaxOnlineUsers = entity.MaxOnlineUsers,
            MaxUsers = entity.MaxUsers,
            SfuConnection = new SfuConnectionSettings
            {
                ConnectTimeoutSeconds = entity.SfuConnectTimeoutSeconds,
                KeepAlivePingSeconds = entity.SfuKeepAlivePingSeconds,
                KeepAliveTimeoutSeconds = entity.SfuKeepAliveTimeoutSeconds,
                CallTimeoutSeconds = entity.SfuCallTimeoutSeconds,
                PingIntervalSeconds = entity.SfuPingIntervalSeconds
            },
            TimeZoneId = entity.TimeZoneId,
            FirstLaunchTime = entity.FirstLaunchTime,
            CurrentLaunchTime = entity.CurrentLaunchTime,
            Host = new OrchestratorHostInfo
            {
                OperatingSystem = entity.OperatingSystem,
                CpuName = entity.CpuName,
                GpuName = entity.GpuName,
                RamSizeMb = entity.RamSizeMb,
                DbProvider = entity.DbProvider,
                MaxDbSizeMb = entity.MaxDbSizeMb
            },
            Region = new OrchestratorRegionInfo
            {
                RegionName = entity.RegionName,
                RegionCode = entity.RegionCode,
                CityName = entity.CityName,
                Locale = entity.Locale
            },
            IsFirstLaunch = entity.IsFirstLaunch
        };
    }

    public static OrchestratorInstanceEntity ToEntity(this OrchestratorInstanceModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return new OrchestratorInstanceEntity
        {
            ServerVersion = model.ServerVersion,
            LicenseKey = model.LicenseKey,
            Ip = model.Ip ?? string.Empty,
            DnsName = model.DnsName,
            Port = model.Port,
            MaxCoordinators = model.MaxCoordinators,
            MaxOnlineUsers = model.MaxOnlineUsers,
            MaxUsers = model.MaxUsers,
            SfuConnectTimeoutSeconds = model.SfuConnection.ConnectTimeoutSeconds,
            SfuKeepAlivePingSeconds = model.SfuConnection.KeepAlivePingSeconds,
            SfuKeepAliveTimeoutSeconds = model.SfuConnection.KeepAliveTimeoutSeconds,
            SfuCallTimeoutSeconds = model.SfuConnection.CallTimeoutSeconds,
            SfuPingIntervalSeconds = model.SfuConnection.PingIntervalSeconds,
            TimeZoneId = model.TimeZoneId,
            FirstLaunchTime = model.FirstLaunchTime,
            CurrentLaunchTime = model.CurrentLaunchTime,
            OperatingSystem = model.Host.OperatingSystem,
            CpuName = model.Host.CpuName,
            GpuName = model.Host.GpuName,
            RamSizeMb = model.Host.RamSizeMb,
            DbProvider = model.Host.DbProvider,
            MaxDbSizeMb = model.Host.MaxDbSizeMb,
            RegionName = model.Region.RegionName,
            RegionCode = model.Region.RegionCode,
            CityName = model.Region.CityName,
            Locale = model.Region.Locale,
            IsFirstLaunch = model.IsFirstLaunch
        };
    }
}
