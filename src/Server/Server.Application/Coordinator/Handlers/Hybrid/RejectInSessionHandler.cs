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

public class RejectInSessionHandler : HybridSessionRequestHandler<RejectUserRequest>
{
    public RejectInSessionHandler(IHybridSessionManager sessionManager, IMessageSender messageSender)
        : base(sessionManager, messageSender)
    {
    }

    protected override int GetSessionId(RejectUserRequest request) => request.SessionId;

    protected override async Task<BaseMessage> HandleAsync(RejectUserRequest request, IHybridSession session) =>
        request.Answer(await session.PeerManager.RejectPeerAsync(request.SenderId, request.TargetUserId, request.Reason), request.ToResponse);
}
