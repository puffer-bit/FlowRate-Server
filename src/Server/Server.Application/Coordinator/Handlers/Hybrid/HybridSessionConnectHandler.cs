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

public class HybridSessionConnectHandler : HybridSessionRequestHandler<HybridSessionConnectTransportRequest>
{
    public HybridSessionConnectHandler(IHybridSessionManager sessionManager, IMessageSender messageSender)
        : base(sessionManager, messageSender)
    {
    }

    protected override int GetSessionId(HybridSessionConnectTransportRequest request) => request.SessionId;

    protected override bool DrivesOwnPeer => true;

    protected override async Task<BaseMessage> HandleAsync(HybridSessionConnectTransportRequest request, IHybridSession session) =>
        request.ToResponse(await session.PeerManager.ConnectPeerAsync(request.SenderId, request.DtlsParameters));

    protected override BaseMessage SessionNotFound(HybridSessionConnectTransportRequest request) =>
        request.ToResponse(HybridSessionConnectTransportResult.SessionNotExists);

    public override BaseMessage CreateFailureResponse(HybridSessionConnectTransportRequest request) =>
        request.ToResponse(HybridSessionConnectTransportResult.InternalError);
}
