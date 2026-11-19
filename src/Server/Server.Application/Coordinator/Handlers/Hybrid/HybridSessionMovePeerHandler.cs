using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.SessionActions.HybridSession;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Roles;
using Server.Application.Coordinator.Sessions.Hybrid;

namespace Server.Application.Coordinator.Handlers.Hybrid;

/// <summary>
/// Moves a peer to another hybrid session. <see cref="RequestPolicies"/> checks the session the peer leaves; the
/// target is checked here: the sender must be allowed to move into it, and the moved user must see it.
/// </summary>
public class HybridSessionMovePeerHandler : HybridSessionRequestHandler<HybridSessionMovePeerRequest>
{
    private readonly IHybridSessionManager _sessionManager;
    private readonly ICoordinatorPermissions _permissions;

    public HybridSessionMovePeerHandler(IHybridSessionManager sessionManager, ICoordinatorPermissions permissions,
        IMessageSender messageSender)
        : base(sessionManager, messageSender)
    {
        _sessionManager = sessionManager;
        _permissions = permissions;
    }

    protected override int GetSessionId(HybridSessionMovePeerRequest request) => request.SessionId;

    protected override async Task<BaseMessage> HandleAsync(HybridSessionMovePeerRequest request, IHybridSession session)
    {
        if (!_sessionManager.TryGetSession(request.TargetSessionId, out var target))
            return request.ToError(ErrorCode.SessionNotFound);
        if (!_permissions.HasIn(request.SenderId, target.Model, Permission.ViewSessions | Permission.MoveMembers))
            return request.ToError(ErrorCode.InsufficientPermissions);
        // Joining needs no JoinSessions of the moved user: the sender decides that
        if (!_permissions.HasIn(request.TargetUserId, target.Model, Permission.ViewSessions))
            return request.ToError(ErrorCode.InsufficientPermissions);

        return request.Answer(await _sessionManager.MovePeerAsync(request.SenderId, request.SessionId,
            request.TargetUserId, request.TargetSessionId), request.ToResponse);
    }
}
