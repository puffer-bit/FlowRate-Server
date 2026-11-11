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

public class LeaveSessionHandler : HybridSessionRequestHandler<LeaveSessionRequest>
{
    public LeaveSessionHandler(IHybridSessionManager sessionManager, IMessageSender messageSender)
        : base(sessionManager, messageSender)
    {
        _callSessions = sessionManager;
    }

    private readonly IHybridSessionManager _callSessions;

    protected override async Task<BaseMessage> HandleAsync(LeaveSessionRequest request, IHybridSession session, string connectionId)
    {
        if (session.Model.CallDevices != null)
        {
            await _callSessions.EndCallForSessionAsync(request.SessionId, request.SenderId, connectionId);
            return request.ToResponse();
        }
        return await HandleAsync(request, session);
    }

    protected override int GetSessionId(LeaveSessionRequest request) => request.SessionId;

    protected override bool DrivesOwnPeer => true;

    protected override async Task<BaseMessage> HandleAsync(LeaveSessionRequest request, IHybridSession session) =>
        request.Answer(await session.PeerManager.RemovePeerAsync(request.SenderId), request.ToResponse);

    // Only hybrid sessions support this request, so an id of another session type is never found
    protected override ErrorCode SessionNotFound(LeaveSessionRequest request) =>
        request.SessionType == SessionType.Hybrid ? ErrorCode.SessionNotFound : ErrorCode.WrongSessionType;
}
