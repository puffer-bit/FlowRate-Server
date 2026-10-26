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

public class HybridSessionSetScreenShareWatchHandler : HybridSessionRequestHandler<HybridSessionSetScreenShareWatchRequest>
{
    public HybridSessionSetScreenShareWatchHandler(IHybridSessionManager sessionManager, IMessageSender messageSender)
        : base(sessionManager, messageSender)
    {
    }

    protected override int GetSessionId(HybridSessionSetScreenShareWatchRequest request) => request.SessionId;

    protected override bool DrivesOwnPeer => true;

    protected override async Task<BaseMessage> HandleAsync(HybridSessionSetScreenShareWatchRequest request, IHybridSession session)
    {
        var (result, inbound, audioInbound) = await session.PeerManager.SetScreenShareWatchAsync(request.SenderId,
            request.UserId, request.IsWatching);
        return request.ToResponse(result, inbound, audioInbound);
    }

    protected override BaseMessage SessionNotFound(HybridSessionSetScreenShareWatchRequest request) =>
        request.ToResponse(SetScreenShareWatchResult.SessionNotExists);

    public override BaseMessage CreateFailureResponse(HybridSessionSetScreenShareWatchRequest request) =>
        request.ToResponse(SetScreenShareWatchResult.InternalError);
}
