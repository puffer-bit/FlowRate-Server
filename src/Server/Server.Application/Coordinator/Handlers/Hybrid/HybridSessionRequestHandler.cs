using Shared.Enums;
using Shared.Models;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Users;
using Server.Application.Coordinator.Authentication;

namespace Server.Application.Coordinator.Handlers.Hybrid;

/// <summary>
/// A request aimed at one hybrid session: find the session, act on it and answer the sender.
/// The sender is always the authenticated user of the connection. A user can be connected from several devices,
/// but takes part in a session from one: what drives the media of the peer is taken only from that connection.
/// </summary>
public abstract class HybridSessionRequestHandler<TRequest> : IMessageHandler<TRequest> where TRequest : class
{
    private readonly IHybridSessionManager _sessionManager;
    private readonly IMessageSender _messageSender;

    protected HybridSessionRequestHandler(IHybridSessionManager sessionManager, IMessageSender messageSender)
    {
        _sessionManager = sessionManager;
        _messageSender = messageSender;
    }

    public async Task<HandleMessageResult> HandleMessage(TRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        BaseMessage response;
        if (!_sessionManager.TryGetSession(GetSessionId(request), out var session))
            response = SessionNotFound(request);
        else if (DrivesOwnPeer && request is BaseMessage { SenderId: var senderId }
                 && !session.PeerManager.IsPeerOf(senderId, connectionId))
            response = CreateFailureResponse(request) ?? SessionNotFound(request);
        else
            response = await HandleAsync(request, session, connectionId);

        _messageSender.SendMessageById(connectionId, response);
        return HandleMessageResult.NoError;
    }

    protected abstract int GetSessionId(TRequest request);

    /// <summary>The request drives the peer of the sender, so only the connection the peer is on may send it.</summary>
    protected virtual bool DrivesOwnPeer => false;

    protected virtual Task<BaseMessage> HandleAsync(TRequest request, IHybridSession session, string connectionId) =>
        HandleAsync(request, session);

    protected abstract Task<BaseMessage> HandleAsync(TRequest request, IHybridSession session);

    protected abstract BaseMessage SessionNotFound(TRequest request);

    public abstract BaseMessage? CreateFailureResponse(TRequest request);
}
