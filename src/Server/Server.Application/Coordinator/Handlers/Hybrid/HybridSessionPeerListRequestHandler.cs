using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.SessionActions.HybridSession;
using Server.Application.Mappers;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Users;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Messaging;

namespace Server.Application.Coordinator.Handlers.Hybrid;

public class HybridSessionPeerListRequestHandler : HybridSessionRequestHandler<HybridSessionPeerListRequest>
{
    public HybridSessionPeerListRequestHandler(IHybridSessionManager sessionManager, IMessageSender messageSender)
        : base(sessionManager, messageSender)
    {
    }

    protected override int GetSessionId(HybridSessionPeerListRequest request) => request.SessionId;

    protected override Task<BaseMessage> HandleAsync(HybridSessionPeerListRequest request, IHybridSession session)
    {
        var peers = session.PeerManager.GetAllPeerSnapshots()
            .ToDictionary(peer => peer.Key, peer => peer.Value.ToDto());

        return Task.FromResult<BaseMessage>(request.ToResponse(PeerListRequestResult.NoError, peers));
    }

    protected override BaseMessage SessionNotFound(HybridSessionPeerListRequest request) =>
        request.ToResponse(PeerListRequestResult.SessionNotExists);

    public override BaseMessage CreateFailureResponse(HybridSessionPeerListRequest request) =>
        request.ToResponse(PeerListRequestResult.InternalError);
}
