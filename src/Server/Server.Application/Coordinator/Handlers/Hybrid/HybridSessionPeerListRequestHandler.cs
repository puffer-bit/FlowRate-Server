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
    private readonly Server.Application.Coordinator.Sessions.ISessionManager? _sessions;

    /// <param name="sessions">Where the chat of a call is found; without it only those in the call read its peers.</param>
    public HybridSessionPeerListRequestHandler(IHybridSessionManager sessionManager, IMessageSender messageSender,
        Server.Application.Coordinator.Sessions.ISessionManager? sessions = null)
        : base(sessionManager, messageSender)
    {
        _sessions = sessions;
    }

    protected override int GetSessionId(HybridSessionPeerListRequest request) => request.SessionId;

    // Members of the chat see who is in its call before joining, as they get its peers' changes; on every
    // device, also on one that is not the device of the call
    protected override bool MayReadCall(HybridSessionPeerListRequest request, IHybridSession session) =>
        _sessions != null && _sessions.TryGetSessionModel(session.Model.Id, out var chat)
        && chat is Server.Core.Models.TextChat.ChatSessionModel { IsPrivate: true } chatOfCall
        && chatOfCall.MemberIds.Contains(request.SenderId);

    protected override Task<BaseMessage> HandleAsync(HybridSessionPeerListRequest request, IHybridSession session)
    {
        var peers = session.PeerManager.GetAllPeerSnapshots()
            .ToDictionary(peer => peer.Key, peer => peer.Value.ToDto());

        return Task.FromResult<BaseMessage>(request.ToResponse(peers));
    }
}
