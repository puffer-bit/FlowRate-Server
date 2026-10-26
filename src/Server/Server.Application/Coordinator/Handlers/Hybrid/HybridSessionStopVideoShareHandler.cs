using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.SessionActions.HybridSession;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Users;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Messaging;

namespace Server.Application.Coordinator.Handlers.Hybrid;

public class HybridSessionStopVideoShareHandler : HybridSessionRequestHandler<HybridSessionStopVideoRequest>
{
    public HybridSessionStopVideoShareHandler(IHybridSessionManager sessionManager, IMessageSender messageSender)
        : base(sessionManager, messageSender)
    {
    }

    protected override int GetSessionId(HybridSessionStopVideoRequest request) => request.SessionId;

    protected override async Task<BaseMessage> HandleAsync(HybridSessionStopVideoRequest request, IHybridSession session) =>
        request.ToResponse(await session.PeerManager.StopVideoShareAsync(request.SenderId, request.SenderId));

    protected override BaseMessage SessionNotFound(HybridSessionStopVideoRequest request) =>
        request.ToResponse(StopVideoShareResult.PeerNotExists);

    public override BaseMessage CreateFailureResponse(HybridSessionStopVideoRequest request) =>
        request.ToResponse(StopVideoShareResult.InternalError);
}
