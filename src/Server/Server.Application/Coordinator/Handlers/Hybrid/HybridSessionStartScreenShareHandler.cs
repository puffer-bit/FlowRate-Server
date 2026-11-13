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

public class HybridSessionStartScreenShareHandler : HybridSessionRequestHandler<HybridSessionStartScreenShareRequest>
{
    public HybridSessionStartScreenShareHandler(IHybridSessionManager sessionManager, IMessageSender messageSender)
        : base(sessionManager, messageSender)
    {
    }

    protected override int GetSessionId(HybridSessionStartScreenShareRequest request) => request.SessionId;

    protected override bool DrivesOwnPeer => true;

    protected override async Task<BaseMessage> HandleAsync(HybridSessionStartScreenShareRequest request, IHybridSession session)
    {
        var (result, outbound, audioOutbound) =
            await session.PeerManager.StartScreenShareAsync(request.SenderId, request.WithAudio);
        return request.Answer(result, () => request.ToResponse(outbound!, audioOutbound));
    }
}
