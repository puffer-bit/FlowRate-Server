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

public class BanInSessionHandler : HybridSessionRequestHandler<BanInSessionRequest>
{
    public BanInSessionHandler(IHybridSessionManager sessionManager, IMessageSender messageSender)
        : base(sessionManager, messageSender)
    {
    }

    protected override int GetSessionId(BanInSessionRequest request) => request.SessionId;

    protected override async Task<BaseMessage> HandleAsync(BanInSessionRequest request, IHybridSession session) =>
        request.ToResponse(await session.PeerManager.BanPeerAsync(request.SenderId, request.UserTargetId, request.Reason));

    // Only hybrid sessions support this request, so an id of another session type is never found
    protected override BaseMessage SessionNotFound(BanInSessionRequest request) =>
        request.ToResponse(request.SessionType == SessionType.Hybrid
            ? BanFromSessionResult.SessionNotExists
            : BanFromSessionResult.UnknownSessionType);

    public override BaseMessage CreateFailureResponse(BanInSessionRequest request) =>
        request.ToResponse(BanFromSessionResult.InternalError);
}
