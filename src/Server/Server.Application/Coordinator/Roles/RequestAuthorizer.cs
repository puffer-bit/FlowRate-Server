using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Models;

namespace Server.Application.Coordinator.Roles;

/// <summary>Decides whether the sender of a request may make it, after <see cref="RequestPolicies"/>.</summary>
public sealed class RequestAuthorizer
{
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
            // Members in the call may do everything in it; the call checks kicks against the group owner.
            // Members of the chat see who is in its call before joining, as they get its peers' changes
            return callDevices.ContainsKey(message.SenderId)
                   || message is Shared.Models.Requests.SessionActions.HybridSession.HybridSessionPeerListRequest
                   && _sessions.TryGetSessionModel(mediaSessionId, out var callChat)
                   && callChat is Server.Core.Models.TextChat.ChatSessionModel { IsPrivate: true } chatOfCall
                   && chatOfCall.MemberIds.Contains(message.SenderId);
        // Server roles do not rank group members: the call kicks only for the group owner
        if (message is Shared.Models.Requests.SessionActions.Generic.KickFromSessionRequest kick
            && _hybridSessions.TryGetSession(kick.SessionId, out var kickSession)
            && kickSession.Model.CallDevices is { } kickDevices)
            return kickDevices.ContainsKey(message.SenderId);
        if (RequestPolicies.Of(message) is not { } policy)
            return false;

        // A user moved here takes the place kept for them without the right to join on their own
        if (message is Shared.Models.Requests.SessionActions.HybridSession.HybridSessionJoinRequest join
            && _hybridSessions.TryGetSession(join.SessionId, out var moveTarget)
            && moveTarget.PeerManager.IsAwaitingMove(message.SenderId))
            policy = policy with { Required = policy.Required & ~Permission.JoinSessions };

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
        if (!byRole)
            return false;

        if (policy.TargetUserId is not { } targetId || targetId == senderId)
            return true;

        // A role acts only on users below it
        return await _permissions.HighestPositionOfAsync(senderId) > await _permissions.HighestPositionOfAsync(targetId);
    }
}
