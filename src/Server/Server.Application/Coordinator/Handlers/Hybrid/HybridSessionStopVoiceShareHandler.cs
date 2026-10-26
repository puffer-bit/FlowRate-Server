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

public class HybridSessionStopVoiceShareHandler : HybridSessionRequestHandler<HybridSessionStopVoiceRequest>
{
    public HybridSessionStopVoiceShareHandler(IHybridSessionManager sessionManager, IMessageSender messageSender)
        : base(sessionManager, messageSender)
    {
    }

    protected override int GetSessionId(HybridSessionStopVoiceRequest request) => request.SessionId;

    protected override async Task<BaseMessage> HandleAsync(HybridSessionStopVoiceRequest request, IHybridSession session) =>
        request.ToResponse(await session.PeerManager.StopVoiceShareAsync(request.SenderId, request.SenderId));

    protected override BaseMessage SessionNotFound(HybridSessionStopVoiceRequest request) =>
        request.ToResponse(StopVoiceShareResult.PeerNotExists);

    public override BaseMessage CreateFailureResponse(HybridSessionStopVoiceRequest request) =>
        request.ToResponse(StopVoiceShareResult.InternalError);
}
