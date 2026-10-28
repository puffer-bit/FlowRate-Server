using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.Auth;
using Shared.Models.Requests.Coordinator;
using Shared.Models.Requests.Coordinator.Security;
using Shared.Models.Requests.Heartbeat;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Models.Requests.SessionActions.Generic;
using Shared.Models.Requests.SessionActions.HybridSession;
using static Server.Application.Coordinator.Roles.RequestPolicy;

namespace Server.Application.Coordinator.Roles;

/// <summary>
/// What each request needs. Every request is listed, open ones too, so a new request is a decision rather than
/// a gap: an unlisted one is refused.
/// </summary>
public static class RequestPolicies
{
    public static RequestPolicy? Of(BaseMessage message) => message switch
    {
        AuthRequest or HeartbeatRequest => Open,

        CoordinatorInfoRequest or CoordinatorUserInfoRequest or CoordinatorUserSearchRequest
            or RegisterPushTokenRequest or UnregisterPushTokenRequest or MediaTokenRequest => Open,
        CoordinatorChangeUsernameRequest request => OnUser(request.SenderId, request.TargetUserId),
        CoordinatorSetAvatarRequest request => OnUser(request.SenderId, request.TargetUserId),
        CoordinatorSetUserStatusRequest => Open,
        // Acting on oneself is refused by the handlers
        CoordinatorSetUserStateRequest request => Require(Permission.BanMembers).Against(request.TargetUserId),
        CoordinatorDeleteUserRequest request => Require(Permission.BanMembers).Against(request.TargetUserId),
        CoordinatorSettingsRequest or CoordinatorUserListRequest => Require(Permission.ViewCoordinatorSettings),
        CoordinatorUpdateSettingsRequest => Require(Permission.ManageCoordinatorSettings),

        // Role hierarchy depends on the role and the user, so the role service checks it
        CoordinatorRolesRequest => Open,
        CoordinatorCreateRoleRequest or CoordinatorUpdateRoleRequest or CoordinatorDeleteRoleRequest
            or CoordinatorReorderRolesRequest or CoordinatorAssignRoleRequest or CoordinatorUnassignRoleRequest
            => Require(Permission.ManageRoles),

        CreateSessionRequest => Require(Permission.CreateSessions),
        DeleteSessionRequest request => InSession(request.SessionId, Permission.ManageSessions),
        ChangeHybridSessionSettingsRequest request => InSession(request.SessionId, Permission.ManageSessions),
        // Which roles and permissions the sender may change, the handler checks
        ChangeSessionPermissionsRequest request =>
            InSession(request.SessionId, Permission.ManageSessions | Permission.ManageRoles),
        CreateSessionCategoryRequest or RenameSessionCategoryRequest or DeleteSessionCategoryRequest
            or MoveSessionRequest or MoveSessionCategoryRequest => Require(Permission.ManageSessions),
        // A session the sender may not see shows only to roles that see it; leaving is never refused
        JoinSessionRequest request => InSession(request.SessionId, Permission.ViewSessions),
        LeaveSessionRequest or SessionInfoRequest => Open,

        KickFromSessionRequest request =>
            InSession(request.SessionId, Permission.KickMembers).Against(request.TargetUserId),
        BanInSessionRequest request =>
            InSession(request.SessionId, Permission.BanMembers).Against(request.TargetUserId),
        ApproveUserRequest request => InSession(request.SessionId, Permission.ApproveMembers),
        RejectUserRequest request => InSession(request.SessionId, Permission.ApproveMembers),

        HybridSessionJoinRequest request => InSession(request.SessionId, View | Permission.JoinSessions),
        HybridSessionStartVoiceRequest request => InSession(request.SessionId, View | Permission.Speak),
        HybridSessionStartVideoRequest request => InSession(request.SessionId, View | Permission.ShareVideo),
        HybridSessionStartScreenShareRequest request => InSession(request.SessionId, View | Permission.ShareVideo),
        HybridSessionConnectTransportRequest request => InSession(request.SessionId, View),
        HybridSessionPeerListRequest request => InSession(request.SessionId, View),
        HybridSessionSetScreenShareWatchRequest request => InSession(request.SessionId, View),
        HybridSessionResumeInboundsRequest request => InSession(request.SessionId, View),
        // Stopping and muting only take away, so they stay open to whoever is inside
        HybridSessionLeaveRequest or HybridSessionStopVideoRequest or HybridSessionStopScreenShareRequest
            or HybridSessionStopVoiceRequest or HybridSessionSetMicrophoneMuteRequest
            or HybridSessionSetSoundMuteRequest or HybridSessionSetAfkRequest => Open,
        HybridSessionPokeRequest request => InSession(request.SessionId, View | Permission.PokeMembers),

        SendMessageRequest request => InSession(request.ChatId, View | Permission.SendMessages
            | (HasAttachments(request) ? Permission.AttachFiles : Permission.None)),
        EditMessageRequest request => InSession(request.ChatId, View | Permission.SendMessages),
        // Deleting messages of others needs ManageMessages; the chat checks it, knowing the author
        DeleteMessageRequest request => InSession(request.ChatId, View),
        ChatSessionHistoryRequest request => InSession(request.ChatId, View),
        ChatSessionMessageRequest request => InSession(request.ChatId, View),
        ChatSessionHistoryPageRequest request => InSession(request.ChatId, View),
        ChatSessionChangesRequest request => InSession(request.ChatId, View),
        ChatMarkReadRequest request => InSession(request.ChatId, View),
        ChatReadStatesRequest => Open,

        // The sender's own account; changes are proven with the current password and code by the handlers
        UserSecurityInfoRequest or UserSetPasswordRequest or UserTwoFactorSetupRequest or UserTwoFactorEnableRequest
            or UserTwoFactorDisableRequest or UserPasskeyOptionsRequest or UserPasskeyRegisterRequest
            or UserPasskeyDeleteRequest or UserSessionsRequest or UserSessionRevokeRequest
            or UserIdentityResetRequest => Open,

        _ => null
    };

    /// <summary>What a frozen user may still do: stay connected and read.</summary>
    public static bool IsAllowedWhenFrozen(BaseMessage message) => message switch
    {
        AuthRequest or HeartbeatRequest => true,

        CoordinatorInfoRequest or CoordinatorUserInfoRequest or CoordinatorUserSearchRequest
            or CoordinatorSettingsRequest or CoordinatorUserListRequest or CoordinatorRolesRequest
            or RegisterPushTokenRequest or UnregisterPushTokenRequest or MediaTokenRequest => true,

        JoinSessionRequest request => request.SessionType == SessionType.Chat,
        LeaveSessionRequest or SessionInfoRequest or HybridSessionLeaveRequest => true,

        ChatSessionHistoryRequest or ChatSessionMessageRequest or ChatSessionHistoryPageRequest
            or ChatSessionChangesRequest or ChatReadStatesRequest or ChatMarkReadRequest => true,

        // Securing one's own account is never held back
        UserSecurityInfoRequest or UserSetPasswordRequest or UserTwoFactorSetupRequest or UserTwoFactorEnableRequest
            or UserTwoFactorDisableRequest or UserPasskeyOptionsRequest or UserPasskeyRegisterRequest
            or UserPasskeyDeleteRequest or UserSessionsRequest or UserSessionRevokeRequest
            or UserIdentityResetRequest => true,

        _ => false
    };

    private const Permission View = Permission.ViewSessions;

    // An invalid request has no message, and is refused before it is handled
    private static bool HasAttachments(SendMessageRequest request) =>
        request.Message is { } message
        && (message.Images.Count > 0 || message.Video.Count > 0 || message.Files.Count > 0);

    private static RequestPolicy OnUser(int senderId, int? userId) =>
        userId is { } id && id != senderId ? Require(Permission.ManageUsers).Against(id) : Open;
}
