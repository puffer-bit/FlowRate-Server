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

public class HybridSessionStartVoiceShareHandler : HybridSessionRequestHandler<HybridSessionStartVoiceRequest>
{
    public HybridSessionStartVoiceShareHandler(IHybridSessionManager sessionManager, IMessageSender messageSender)
        : base(sessionManager, messageSender)
    {
    }

    protected override int GetSessionId(HybridSessionStartVoiceRequest request) => request.SessionId;

    protected override bool DrivesOwnPeer => true;

    protected override async Task<BaseMessage> HandleAsync(HybridSessionStartVoiceRequest request, IHybridSession session)
    {
        var (result, outbound) = await session.PeerManager.StartVoiceShareAsync(request.SenderId);
        return request.ToResponse(result, outbound);
    }

    protected override BaseMessage SessionNotFound(HybridSessionStartVoiceRequest request) =>
        request.ToResponse(StartVoiceShareResult.SessionNotExists);

    public override BaseMessage CreateFailureResponse(HybridSessionStartVoiceRequest request) =>
        request.ToResponse(StartVoiceShareResult.InternalError);
}
