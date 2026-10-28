using System.Runtime.InteropServices;
using Shared.Models.DataTransferObjects;
using Server.Application.Coordinator;

namespace Server.Application.Mappers;

public static class CoordinatorMapperExtensions
{
    private static readonly string ServerPlatform =
        $"{OsName()} {RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";

    public static CoordinatorSessionDTO ToDto(this ICoordinatorInstanceContext context, int connectedUsersCount)
    {
        var model = context.Model;

        return new CoordinatorSessionDTO
        {
            Id = model.Id,
            Name = model.Name,
            Address = model.Ip,
            Port = model.Port,
            ConnectedUsersCount = connectedUsersCount,
            ServerVersion = context.ServerVersion,
            ServerPlatform = ServerPlatform,
            CreateTime = model.CreateTime,
            FirstLaunchTime = model.FirstLaunchTime,
            CurrentLaunchTime = model.CurrentLaunchTime,
            MaxOnlineUsers = model.MaxOnlineUsers,
            MaxUsers = model.MaxUsers,
            IsMOTDEnabled = model.Motd.IsEnabled,
            MOTDText = model.Motd.IsEnabled ? model.Motd.Text : null,
            IsPasswordProtected = !string.IsNullOrEmpty(model.PasswordHash),
            HeartbeatIntervalSeconds = model.Heartbeat.IntervalSeconds,
            HeartbeatTimeoutSeconds = model.Heartbeat.TimeoutSeconds,
            MaxFileSizeBytes = model.Media.MaxFileSizeBytes,
            StorageQuotaBytes = model.Media.StorageQuotaBytes,
            SettingsRevision = model.Revision
        };
    }

    private static string OsName() =>
        OperatingSystem.IsLinux() ? "Linux"
        : OperatingSystem.IsWindows() ? "Windows"
        : OperatingSystem.IsMacOS() ? "macOS"
        : RuntimeInformation.OSDescription;
}
