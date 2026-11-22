using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.SessionActions.Generic;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Users;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Messaging;

namespace Server.Application.Coordinator.Handlers.Hybrid;

public class KickFromSessionHandler : HybridSessionRequestHandler<KickFromSessionRequest>
{
    public KickFromSessionHandler(IHybridSessionManager sessionManager, IMessageSender messageSender)
        : base(sessionManager, messageSender)
    {
        _calls = sessionManager;
    }

    private readonly IHybridSessionManager _calls;

    // In a chat call the group owner kicks; the member leaves the call and may join it again
    protected override async Task<BaseMessage> HandleAsync(KickFromSessionRequest request, IHybridSession session,
        string connectionId) =>
        session.Model.CallDevices != null
            ? request.Answer(await _calls.KickFromCallAsync(request.SessionId, request.SenderId, connectionId,
                request.TargetUserId, request.Reason), request.ToResponse)
            : await HandleAsync(request, session);

    protected override int GetSessionId(KickFromSessionRequest request) => request.SessionId;

    protected override async Task<BaseMessage> HandleAsync(KickFromSessionRequest request, IHybridSession session) =>
        request.Answer(await session.PeerManager.KickPeerAsync(request.SenderId, request.TargetUserId, request.Reason), request.ToResponse);
}
