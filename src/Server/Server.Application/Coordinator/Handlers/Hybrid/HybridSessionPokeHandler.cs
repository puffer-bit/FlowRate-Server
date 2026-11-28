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

public class HybridSessionPokeHandler : HybridSessionRequestHandler<HybridSessionPokeRequest>
{
    public HybridSessionPokeHandler(IHybridSessionManager sessionManager, IMessageSender messageSender)
        : base(sessionManager, messageSender)
    {
    }

    protected override int GetSessionId(HybridSessionPokeRequest request) => request.SessionId;

    protected override async Task<BaseMessage> HandleAsync(HybridSessionPokeRequest request, IHybridSession session) =>
        request.Answer(await session.PeerManager.PokeAsync(request.SenderId, request.TargetUserId, request.Message), request.ToResponse);
}
