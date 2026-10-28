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

public class HybridSessionJoinHandler : HybridSessionRequestHandler<HybridSessionJoinRequest>
{
    public HybridSessionJoinHandler(IHybridSessionManager sessionManager, IMessageSender messageSender)
        : base(sessionManager, messageSender)
    {
    }

    protected override int GetSessionId(HybridSessionJoinRequest request) => request.SessionId;

    // Joining from another device takes the peer over from the one the user was on
    protected override async Task<BaseMessage> HandleAsync(HybridSessionJoinRequest request, IHybridSession session,
        string connectionId)
    {
        var (result, transportData) = await session.PeerManager.AddPeerAsync(request.SenderId,
            request.ClientEpoch ?? string.Empty, connectionId);
        return request.Answer(result, () => request.ToResponse(transportData!));
    }

    protected override async Task<BaseMessage> HandleAsync(HybridSessionJoinRequest request, IHybridSession session)
    {
        var (result, transportData) = await session.PeerManager.AddPeerAsync(request.SenderId,
            request.ClientEpoch ?? string.Empty);
        return request.Answer(result, () => request.ToResponse(transportData!));
    }
}
