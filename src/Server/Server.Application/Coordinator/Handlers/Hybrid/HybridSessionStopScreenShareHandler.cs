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

public class HybridSessionStopScreenShareHandler : HybridSessionRequestHandler<HybridSessionStopScreenShareRequest>
{
    public HybridSessionStopScreenShareHandler(IHybridSessionManager sessionManager, IMessageSender messageSender)
        : base(sessionManager, messageSender)
    {
    }

    protected override int GetSessionId(HybridSessionStopScreenShareRequest request) => request.SessionId;

    protected override async Task<BaseMessage> HandleAsync(HybridSessionStopScreenShareRequest request, IHybridSession session) =>
        request.Answer(await session.PeerManager.StopScreenShareAsync(request.SenderId, request.SenderId), request.ToResponse);
}
