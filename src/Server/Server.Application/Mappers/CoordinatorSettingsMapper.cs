using Server.Application.Abstractions.Sfu;
using Shared.Models.DataTransferObjects;
using Server.Application.Orchestrator.Settings;
using Server.Core.Models.Coordinator;
using Server.Core.Models.Coordinator.Settings;
using Server.Core.Models.Orchestrator;

namespace Server.Application.Mappers;

public static class CoordinatorSettingsMapper
{
    public static CoordinatorSettingsDTO ToSettingsDto(this CoordinatorInstanceModel model) => new()
    {
        Revision = model.Revision,
        Name = model.Name,
        Ip = model.Ip,
        Port = model.Port,
        IsPasswordProtected = !string.IsNullOrEmpty(model.PasswordHash),
        MaxOnlineUsers = model.MaxOnlineUsers,
        MaxUsers = model.MaxUsers,
        IsMOTDEnabled = model.Motd.IsEnabled,
        MOTDText = model.Motd.Text,
        AllowedIps = model.AccessList.AllowedIps.ToList(),
        BannedIps = model.AccessList.BannedIps.ToList(),
        HeartbeatIntervalSeconds = model.Heartbeat.IntervalSeconds,
        HeartbeatTimeoutSeconds = model.Heartbeat.TimeoutSeconds,
        MaxFileSizeBytes = model.Media.MaxFileSizeBytes,
        StorageQuotaBytes = model.Media.StorageQuotaBytes,
        MaxUnattachedUploadsPerUser = model.Uploads.MaxUnattachedUploadsPerUser,
        UnattachedUploadLifetimeHours = model.Uploads.UnattachedLifetimeHours,
        MediaTokenLifetimeMinutes = model.Uploads.TokenLifetimeMinutes,
        RateLimits = model.RateLimits.ToDto(),
        SFUNodeIds = model.SfuNodeIds.ToList()
    };

    public static CoordinatorRateLimitsDTO ToDto(this CoordinatorRateLimitSettings limits) => new()
    {
        Global = limits.Global.ToDto(),
        AuthenticationPerIp = limits.AuthenticationPerIp.ToDto(),
        SessionManagement = limits.SessionManagement.ToDto(),
        SessionLayout = limits.SessionLayout.ToDto(),
        SessionMedia = limits.SessionMedia.ToDto(),
        MicrophoneMute = limits.MicrophoneMute.ToDto(),
        InboundResume = limits.InboundResume.ToDto(),
        Moderation = limits.Moderation.ToDto(),
        ChatWrite = limits.ChatWrite.ToDto(),
        ChatRead = limits.ChatRead.ToDto(),
        PushRegistration = limits.PushRegistration.ToDto(),
        MediaToken = limits.MediaToken.ToDto(),
        Query = limits.Query.ToDto(),
        Heartbeat = limits.Heartbeat.ToDto(),
        MaxRejectionsPerWindow = limits.MaxRejectionsPerWindow,
        RejectionWindowSeconds = limits.RejectionWindowSeconds,
        MaxConnectionsPerIp = limits.MaxConnectionsPerIp
    };

    private static RequestRateDTO ToDto(this RequestRateSetting rate) =>
        new() { Count = rate.Count, PeriodSeconds = rate.PeriodSeconds };

    public static SFUNodeDTO ToDto(this SfuNodeSettingsModel node, SfuNodeStatus? status) => new()
    {
        Id = node.Id, Name = node.Name, Host = node.Host, Port = node.Port, IsEnabled = node.IsEnabled,
        ConnectionState = status?.State,
        PingMs = status?.RoundTripTime is { } roundTrip ? Math.Round(roundTrip.TotalMilliseconds, 1) : null,
        LastPingAtUtc = status?.LastPingAtUtc,
        ConnectionError = status?.Error
    };

    public static CoordinatorSettingsChange ToChange(this CoordinatorSettingsUpdateDTO dto) => new()
    {
        Name = dto.Name,
        Ip = dto.Ip,
        Port = dto.Port,
        NewPassword = dto.NewPassword,
        RemovePassword = dto.RemovePassword,
        MaxOnlineUsers = dto.MaxOnlineUsers,
        MaxUsers = dto.MaxUsers,
        IsMotdEnabled = dto.IsMOTDEnabled,
        MotdText = dto.MOTDText,
        AllowedIps = dto.AllowedIps,
        BannedIps = dto.BannedIps,
        HeartbeatIntervalSeconds = dto.HeartbeatIntervalSeconds,
        HeartbeatTimeoutSeconds = dto.HeartbeatTimeoutSeconds,
        MaxFileSizeBytes = dto.MaxFileSizeBytes,
        StorageQuotaBytes = dto.StorageQuotaBytes,
        MaxUnattachedUploadsPerUser = dto.MaxUnattachedUploadsPerUser,
        UnattachedUploadLifetimeHours = dto.UnattachedUploadLifetimeHours,
        MediaTokenLifetimeMinutes = dto.MediaTokenLifetimeMinutes,
        RateLimits = dto.RateLimits.ToChange(),
        SfuNodeIds = dto.SFUNodeIds
    };

    public static RateLimitsChange ToChange(this CoordinatorRateLimitsDTO dto) => new()
    {
        Global = dto.Global.ToChange(),
        AuthenticationPerIp = dto.AuthenticationPerIp.ToChange(),
        SessionManagement = dto.SessionManagement.ToChange(),
        SessionLayout = dto.SessionLayout.ToChange(),
        SessionMedia = dto.SessionMedia.ToChange(),
        MicrophoneMute = dto.MicrophoneMute.ToChange(),
        InboundResume = dto.InboundResume.ToChange(),
        Moderation = dto.Moderation.ToChange(),
        ChatWrite = dto.ChatWrite.ToChange(),
        ChatRead = dto.ChatRead.ToChange(),
        PushRegistration = dto.PushRegistration.ToChange(),
        MediaToken = dto.MediaToken.ToChange(),
        Query = dto.Query.ToChange(),
        Heartbeat = dto.Heartbeat.ToChange(),
        MaxRejectionsPerWindow = dto.MaxRejectionsPerWindow,
        RejectionWindowSeconds = dto.RejectionWindowSeconds,
        MaxConnectionsPerIp = dto.MaxConnectionsPerIp
    };

    private static RequestRateChange ToChange(this RequestRateDTO rate) => new(rate.Count, rate.PeriodSeconds);
}
