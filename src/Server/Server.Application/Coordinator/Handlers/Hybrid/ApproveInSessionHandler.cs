using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.SessionActions.Generic;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Users;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Messaging;

namespace Server.Application.Coordinator.Handlers.Hybrid;

public class ApproveInSessionHandler : HybridSessionRequestHandler<ApproveUserRequest>
{
    public ApproveInSessionHandler(IHybridSessionManager sessionManager, IMessageSender messageSender)
        : base(sessionManager, messageSender)
    {
    }

    protected override int GetSessionId(ApproveUserRequest request) => request.SessionId;

    protected override async Task<BaseMessage> HandleAsync(ApproveUserRequest request, IHybridSession session) =>
        request.Answer(await session.PeerManager.ApprovePeerAsync(request.SenderId, request.TargetUserId), request.ToResponse);

    // Only hybrid sessions support this request, so an id of another session type is never found
    protected override ErrorCode SessionNotFound(ApproveUserRequest request) =>
        request.SessionType == SessionType.Hybrid ? ErrorCode.SessionNotFound : ErrorCode.WrongSessionType;
}
