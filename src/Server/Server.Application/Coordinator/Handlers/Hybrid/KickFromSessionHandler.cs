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

public class KickFromSessionHandler : HybridSessionRequestHandler<KickFromSessionRequest>
{
    public KickFromSessionHandler(IHybridSessionManager sessionManager, IMessageSender messageSender)
        : base(sessionManager, messageSender)
    {
    }

    protected override int GetSessionId(KickFromSessionRequest request) => request.SessionId;

    protected override async Task<BaseMessage> HandleAsync(KickFromSessionRequest request, IHybridSession session) =>
        request.Answer(await session.PeerManager.KickPeerAsync(request.SenderId, request.TargetUserId, request.Reason), request.ToResponse);

    // Only hybrid sessions support this request, so an id of another session type is never found
    protected override ErrorCode SessionNotFound(KickFromSessionRequest request) =>
        request.SessionType == SessionType.Hybrid ? ErrorCode.SessionNotFound : ErrorCode.WrongSessionType;
}
