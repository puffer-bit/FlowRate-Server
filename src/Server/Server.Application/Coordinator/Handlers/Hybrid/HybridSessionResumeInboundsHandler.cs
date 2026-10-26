using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.SessionActions.HybridSession;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Messaging;

namespace Server.Application.Coordinator.Handlers.Hybrid;

public class HybridSessionResumeInboundsHandler : HybridSessionRequestHandler<HybridSessionResumeInboundsRequest>
{
    public HybridSessionResumeInboundsHandler(IHybridSessionManager sessionManager, IMessageSender messageSender)
        : base(sessionManager, messageSender)
    {
    }

    protected override int GetSessionId(HybridSessionResumeInboundsRequest request) => request.SessionId;

    protected override bool DrivesOwnPeer => true;

    protected override async Task<BaseMessage> HandleAsync(HybridSessionResumeInboundsRequest request, IHybridSession session) =>
        request.ToResponse(await session.PeerManager.ResumeInboundsAsync(request.SenderId, request.InboundIds));

    protected override BaseMessage SessionNotFound(HybridSessionResumeInboundsRequest request) =>
        request.ToResponse(ResumeInboundsResult.SessionNotExists);

    public override BaseMessage CreateFailureResponse(HybridSessionResumeInboundsRequest request) =>
        request.ToResponse(ResumeInboundsResult.InternalError);
}
