using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Models;

namespace Server.Application.Coordinator.Roles;

/// <summary>Decides whether the sender of a request may make it, after <see cref="RequestPolicies"/>.</summary>
public sealed class RequestAuthorizer
{
    /// <summary>What the host of a hybrid session may do in it, whatever their roles.</summary>
    public const Permission HostPermissions =
        Permission.ManageSessions | Permission.KickMembers | Permission.ApproveMembers;

    private readonly ICoordinatorPermissions _permissions;
    private readonly IHybridSessionManager _hybridSessions;
    private readonly ISessionManager _sessions;

    public RequestAuthorizer(ICoordinatorPermissions permissions, IHybridSessionManager hybridSessions,
        ISessionManager sessions)
    {
        _permissions = permissions;
        _hybridSessions = hybridSessions;
        _sessions = sessions;
    }

    /// <summary>Whether the sender is frozen and the request does more than read.</summary>
    public bool IsFrozenOut(BaseMessage message) =>
        message.SenderId != UserModel.SystemUserId
        && !RequestPolicies.IsAllowedWhenFrozen(message)
        && _permissions.IsFrozen(message.SenderId);

    public async Task<bool> IsAllowedAsync(BaseMessage message)
    {
        if (message is Shared.Models.Requests.SessionActions.HybridSession.HybridSessionRequest
            && RequestPolicies.Of(message)?.SessionId is { } mediaSessionId
            && _hybridSessions.TryGetSession(mediaSessionId, out var callSession)
            && callSession.Model.CallDevices is { } callDevices)
        {
            if (!callDevices.ContainsKey(message.SenderId)) return false;
            return message is not (Shared.Models.Requests.SessionActions.HybridSession.HybridSessionStartVideoRequest
                or Shared.Models.Requests.SessionActions.HybridSession.HybridSessionStartScreenShareRequest);
        }
        if (RequestPolicies.Of(message) is not { } policy)
            return false;

        var senderId = message.SenderId;
        if (senderId == UserModel.SystemUserId || policy == RequestPolicy.Open)
            return true;

        // Private membership is authoritative, including for server administrators.
        if (policy.SessionId is { } privateId && _sessions.TryGetSessionModel(privateId, out var privateScope)
            && privateScope is Server.Core.Models.TextChat.ChatSessionModel { IsPrivate: true } privateChat)
        {
            if (message is Shared.Models.Requests.SessionActions.Generic.ChangeSessionPermissionsRequest
                || message is Shared.Models.Requests.SessionActions.Generic.MoveSessionRequest)
                return false;
            if (message is Shared.Models.Requests.SessionActions.ChatSession.SetMessagePinnedRequest
                && privateChat.Kind == ChatKind.Direct)
                return privateChat.MemberIds.Contains(senderId);
            return privateChat.MemberIds.Contains(senderId) && _permissions.HasIn(senderId, privateChat, policy.Required);
        }

        // A session that does not exist is the handler's to answer; the coordinator permissions decide until then
        var byRole = policy.SessionId is { } scopeId && _sessions.TryGetSessionModel(scopeId, out var scope)
            ? _permissions.HasIn(senderId, scope, policy.Required)
            : _permissions.Has(senderId, policy.Required);
        var byHost = policy.SessionId is { } sessionId
                     && (policy.Required & ~HostPermissions) == 0
                     && _hybridSessions.TryGetSession(sessionId, out var session)
                     && session.Model.HostId == senderId;
        if (!byRole && !byHost)
            return false;

        if (policy.TargetUserId is not { } targetId || targetId == senderId)
            return true;

        // A role acts only on users below it; a host also on users of the same rank, as on plain users
        var sender = await _permissions.HighestPositionOfAsync(senderId);
        var target = await _permissions.HighestPositionOfAsync(targetId);
        return (byRole && sender > target) || (byHost && sender >= target);
    }
}
