using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.SessionActions.HybridSession;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Users;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Roles;

namespace Server.Application.Coordinator.Handlers.Hybrid;

public class HybridSessionJoinHandler : HybridSessionRequestHandler<HybridSessionJoinRequest>
{
    public HybridSessionJoinHandler(IHybridSessionManager sessionManager, IMessageSender messageSender,
        ICoordinatorPermissions? permissions = null)
        : base(sessionManager, messageSender)
    {
        _callSessions = sessionManager;
        _permissions = permissions;
    }

    private readonly IHybridSessionManager _callSessions;
    private readonly ICoordinatorPermissions? _permissions;

    // Who may move others in past the capacity may also come in so themselves; approval still holds
    private bool MayJoinPastCapacity(int userId, IHybridSession session) =>
        session.Model.CallDevices == null
        && _permissions?.HasIn(userId, session.Model, Permission.MoveMembers) == true;

    // Who may let others in needs nobody to let them in
    private bool MayApprove(int userId, IHybridSession session) =>
        _permissions?.HasIn(userId, session.Model, Permission.ApproveMembers) == true;

    protected override int GetSessionId(HybridSessionJoinRequest request) => request.SessionId;

    // Joining from another device takes the peer over from the one the user was on
    protected override async Task<BaseMessage> HandleAsync(HybridSessionJoinRequest request, IHybridSession session,
        string connectionId)
    {
        if (session.Model.CallDevices == null && _callSessions.HasDirectCall(request.SenderId))
            return request.ToError(ErrorCode.Conflict);
        var (result, transportData) = await session.PeerManager.AddPeerAsync(request.SenderId,
            request.ClientEpoch ?? string.Empty, connectionId, MayJoinPastCapacity(request.SenderId, session),
            MayApprove(request.SenderId, session));
        return request.Answer(result, () => request.ToResponse(transportData));
    }

    protected override async Task<BaseMessage> HandleAsync(HybridSessionJoinRequest request, IHybridSession session)
    {
        var (result, transportData) = await session.PeerManager.AddPeerAsync(request.SenderId,
            request.ClientEpoch ?? string.Empty, pastCapacity: MayJoinPastCapacity(request.SenderId, session),
            mayApprove: MayApprove(request.SenderId, session));
        return request.Answer(result, () => request.ToResponse(transportData));
    }
}
