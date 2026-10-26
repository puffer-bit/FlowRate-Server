using Server.Core.Models.Coordinator.Settings;
using Shared.Models;
using Shared.Models.Requests.Auth;
using Shared.Models.Requests.Coordinator;
using Shared.Models.Requests.Coordinator.Security;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Models.Requests.SessionActions.Generic;
using Shared.Models.Requests.SessionActions.HybridSession;
using Shared.Models.Requests.WebRTC;
using Shared.Models.Requests.Heartbeat;

namespace Server.Application.Coordinator.Connections.RateLimiting;

public enum RequestRateGroup
{
    Authentication,
    SessionManagement,
    SessionLayout,
    SessionMedia,
    MicrophoneMute,
    InboundResume,
    Moderation,
    ChatWrite,
    ChatRead,
    PushRegistration,
    MediaToken,
    Query,
    Heartbeat
}

public readonly record struct RateLimit(int Capacity, double RefillPerSecond)
{
    public static RateLimit PerPeriod(int count, TimeSpan period) => new(count, count / period.TotalSeconds);
}

/// <summary>
/// The rate limits of one coordinator. New settings are applied while it runs; whoever keeps token buckets made
/// from these limits watches <see cref="Version"/> and makes them anew when it changes.
/// </summary>
public sealed class RequestRateLimits
{
    public static readonly RequestRateLimits Default = new(CoordinatorRateLimitSettings.Default);

    private volatile CoordinatorRateLimitSettings _settings;
    private int _version;

    public RequestRateLimits(CoordinatorRateLimitSettings settings)
    {
        _settings = settings;
    }

    /// <summary>Grows with every <see cref="Apply"/>.</summary>
    public int Version => Volatile.Read(ref _version);

    public RateLimit Global => From(_settings.Global);
    public RateLimit AuthenticationPerIp => From(_settings.AuthenticationPerIp);
    public int MaxRejectionsPerWindow => _settings.MaxRejectionsPerWindow;
    public TimeSpan RejectionWindow => TimeSpan.FromSeconds(_settings.RejectionWindowSeconds);
    public int MaxConnectionsPerIp => _settings.MaxConnectionsPerIp;

    public void Apply(CoordinatorRateLimitSettings settings)
    {
        _settings = settings;
        Interlocked.Increment(ref _version);
    }

    private static RateLimit From(RequestRateSetting setting) =>
        RateLimit.PerPeriod(setting.Count, TimeSpan.FromSeconds(setting.PeriodSeconds));

    public RateLimit For(RequestRateGroup group)
    {
        var settings = _settings;
        return group switch
        {
            RequestRateGroup.Authentication => From(settings.AuthenticationPerIp),
            RequestRateGroup.SessionManagement => From(settings.SessionManagement),
            RequestRateGroup.SessionLayout => From(settings.SessionLayout),
            RequestRateGroup.SessionMedia => From(settings.SessionMedia),
            RequestRateGroup.MicrophoneMute => From(settings.MicrophoneMute),
            RequestRateGroup.InboundResume => From(settings.InboundResume),
            RequestRateGroup.Moderation => From(settings.Moderation),
            RequestRateGroup.ChatWrite => From(settings.ChatWrite),
            RequestRateGroup.ChatRead => From(settings.ChatRead),
            RequestRateGroup.PushRegistration => From(settings.PushRegistration),
            RequestRateGroup.MediaToken => From(settings.MediaToken),
            RequestRateGroup.Query => From(settings.Query),
            RequestRateGroup.Heartbeat => From(settings.Heartbeat),
            _ => throw new ArgumentOutOfRangeException(nameof(group), group, null)
        };
    }

    public static RequestRateGroup GroupOf(BaseMessage message) => message switch
    {
        AuthRequest => RequestRateGroup.Authentication,
        // A proof can be guessed like a login, so it counts against the same limit per IP
        UserSetPasswordRequest or UserTwoFactorSetupRequest or UserTwoFactorEnableRequest
            or UserTwoFactorDisableRequest or UserPasskeyOptionsRequest or UserPasskeyRegisterRequest
            or UserPasskeyDeleteRequest => RequestRateGroup.Authentication,
        CreateSessionRequest or DeleteSessionRequest or ChangeHybridSessionSettingsRequest
            or ChangeSessionPermissionsRequest
            => RequestRateGroup.SessionManagement,
        CreateSessionCategoryRequest or RenameSessionCategoryRequest or DeleteSessionCategoryRequest
            or MoveSessionRequest or MoveSessionCategoryRequest => RequestRateGroup.SessionLayout,
        JoinSessionRequest or LeaveSessionRequest
            or HybridSessionJoinRequest or HybridSessionLeaveRequest or HybridSessionConnectTransportRequest
            or HybridSessionStartVoiceRequest or HybridSessionStopVoiceRequest
            or HybridSessionStartVideoRequest or HybridSessionStopVideoRequest
            or HybridSessionStartScreenShareRequest or HybridSessionStopScreenShareRequest
            or HybridSessionSetScreenShareWatchRequest
            or WebRtcConnectRequest => RequestRateGroup.SessionMedia,
        HybridSessionSetMicrophoneMuteRequest or HybridSessionSetSoundMuteRequest
            or HybridSessionSetAfkRequest => RequestRateGroup.MicrophoneMute,
        HybridSessionResumeInboundsRequest => RequestRateGroup.InboundResume,
        ApproveUserRequest or RejectUserRequest or KickFromSessionRequest or BanInSessionRequest
            or CoordinatorCreateRoleRequest or CoordinatorUpdateRoleRequest or CoordinatorDeleteRoleRequest
            or CoordinatorReorderRolesRequest or CoordinatorAssignRoleRequest or CoordinatorUnassignRoleRequest
            or CoordinatorSetUserStateRequest or CoordinatorDeleteUserRequest
            => RequestRateGroup.Moderation,
        SendTextMessageRequest or EditTextMessageRequest or DeleteTextMessageRequest
            or HybridSessionPokeRequest => RequestRateGroup.ChatWrite,
        ChatSessionHistoryRequest or ChatSessionMessageRequest or ChatSessionHistoryPageRequest
            or ChatSessionChangesRequest or ChatReadStatesRequest or ChatMarkReadRequest => RequestRateGroup.ChatRead,
        RegisterPushTokenRequest or UnregisterPushTokenRequest => RequestRateGroup.PushRegistration,
        MediaTokenRequest => RequestRateGroup.MediaToken,
        HeartbeatRequest => RequestRateGroup.Heartbeat,
        _ => RequestRateGroup.Query
    };
}
