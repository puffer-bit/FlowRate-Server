using Server.Application.Abstractions.Sfu;
using Server.Application.Orchestrator.Settings;
using Server.Asp.Web.Api.Contracts;
using Server.Core.Models.Coordinator;
using Server.Core.Models.Coordinator.Settings;
using Server.Core.Models.Orchestrator;

namespace Server.Asp.Web.Api;

public static class CoordinatorSettingsContractsMapper
{
    public static CoordinatorSettingsDto ToSettingsDto(this CoordinatorInstanceModel model) => new(
        model.Id,
        model.Revision,
        model.Name,
        model.IsDefault,
        model.Ip,
        model.Port,
        !string.IsNullOrEmpty(model.PasswordHash),
        model.MaxOnlineUsers,
        model.MaxUsers,
        new MotdSettingsDto { IsEnabled = model.Motd.IsEnabled, Text = model.Motd.Text },
        new AccessListSettingsDto { AllowedIps = model.AccessList.AllowedIps, BannedIps = model.AccessList.BannedIps },
        new HeartbeatSettingsDto
        {
            IntervalSeconds = model.Heartbeat.IntervalSeconds, TimeoutSeconds = model.Heartbeat.TimeoutSeconds
        },
        new MediaSettingsDto
        {
            MaxFileSizeBytes = model.Media.MaxFileSizeBytes, StorageQuotaBytes = model.Media.StorageQuotaBytes
        },
        new UploadSettingsDto
        {
            MaxUnattachedPerUser = model.Uploads.MaxUnattachedUploadsPerUser,
            UnattachedLifetimeHours = model.Uploads.UnattachedLifetimeHours,
            TokenLifetimeMinutes = model.Uploads.TokenLifetimeMinutes
        },
        model.RateLimits.ToDto(),
        model.SfuNodeIds);

    public static RateLimitSettingsDto ToDto(this CoordinatorRateLimitSettings limits) => new()
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

    private static RequestRateDto ToDto(this RequestRateSetting rate) =>
        new() { Count = rate.Count, PeriodSeconds = rate.PeriodSeconds };

    public static SfuNodeDto ToDto(this SfuNodeSettingsModel node, SfuNodeStatus? status) =>
        new(node.Id, node.Name, node.Host, node.Port, node.IsSecured, node.IsEnabled, status?.ToDto());

    public static SfuNodeStatusDto ToDto(this SfuNodeStatus status) =>
        new(status.State,
            status.RoundTripTime is { } roundTrip ? Math.Round(roundTrip.TotalMilliseconds, 1) : null,
            status.LastPingAtUtc,
            status.StateChangedAtUtc,
            status.Error,
            status.Load?.SessionCount,
            status.Load?.WorkerCount);

    public static CoordinatorSettingsChange ToChange(this UpdateCoordinatorSettingsDto settings) => new()
    {
        Name = settings.Name,
        Ip = settings.Ip,
        Port = settings.Port,
        NewPassword = settings.NewPassword,
        RemovePassword = settings.RemovePassword,
        MaxOnlineUsers = settings.MaxOnlineUsers,
        MaxUsers = settings.MaxUsers,
        IsMotdEnabled = settings.Motd.IsEnabled,
        MotdText = settings.Motd.Text,
        AllowedIps = settings.AccessList.AllowedIps,
        BannedIps = settings.AccessList.BannedIps,
        HeartbeatIntervalSeconds = settings.Heartbeat.IntervalSeconds,
        HeartbeatTimeoutSeconds = settings.Heartbeat.TimeoutSeconds,
        MaxFileSizeBytes = settings.Media.MaxFileSizeBytes,
        StorageQuotaBytes = settings.Media.StorageQuotaBytes,
        MaxUnattachedUploadsPerUser = settings.Uploads.MaxUnattachedPerUser,
        UnattachedUploadLifetimeHours = settings.Uploads.UnattachedLifetimeHours,
        MediaTokenLifetimeMinutes = settings.Uploads.TokenLifetimeMinutes,
        RateLimits = settings.RateLimits.ToChange(),
        SfuNodeIds = settings.SfuNodeIds
    };

    public static RateLimitsChange ToChange(this RateLimitSettingsDto limits) => new()
    {
        Global = limits.Global.ToChange(),
        AuthenticationPerIp = limits.AuthenticationPerIp.ToChange(),
        SessionManagement = limits.SessionManagement.ToChange(),
        SessionLayout = limits.SessionLayout.ToChange(),
        SessionMedia = limits.SessionMedia.ToChange(),
        MicrophoneMute = limits.MicrophoneMute.ToChange(),
        InboundResume = limits.InboundResume.ToChange(),
        Moderation = limits.Moderation.ToChange(),
        ChatWrite = limits.ChatWrite.ToChange(),
        ChatRead = limits.ChatRead.ToChange(),
        PushRegistration = limits.PushRegistration.ToChange(),
        MediaToken = limits.MediaToken.ToChange(),
        Query = limits.Query.ToChange(),
        Heartbeat = limits.Heartbeat.ToChange(),
        MaxRejectionsPerWindow = limits.MaxRejectionsPerWindow,
        RejectionWindowSeconds = limits.RejectionWindowSeconds,
        MaxConnectionsPerIp = limits.MaxConnectionsPerIp
    };

    private static RequestRateChange ToChange(this RequestRateDto rate) => new(rate.Count, rate.PeriodSeconds);
}
