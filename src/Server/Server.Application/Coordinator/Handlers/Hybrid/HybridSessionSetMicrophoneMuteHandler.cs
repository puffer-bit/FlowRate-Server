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

public class HybridSessionSetMicrophoneMuteHandler : HybridSessionRequestHandler<HybridSessionSetMicrophoneMuteRequest>
{
    public HybridSessionSetMicrophoneMuteHandler(IHybridSessionManager sessionManager, IMessageSender messageSender)
        : base(sessionManager, messageSender)
    {
    }

    protected override int GetSessionId(HybridSessionSetMicrophoneMuteRequest request) => request.SessionId;

    protected override async Task<BaseMessage> HandleAsync(HybridSessionSetMicrophoneMuteRequest request, IHybridSession session) =>
        request.ToResponse(await session.PeerManager.SetMicrophoneMutedAsync(request.SenderId, request.SenderId, request.IsMuted));

    protected override BaseMessage SessionNotFound(HybridSessionSetMicrophoneMuteRequest request) =>
        request.ToResponse(SetMicrophoneMuteResult.SessionNotExists);

    public override BaseMessage CreateFailureResponse(HybridSessionSetMicrophoneMuteRequest request) =>
        request.ToResponse(SetMicrophoneMuteResult.InternalError);
}
