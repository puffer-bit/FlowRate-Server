using Microsoft.Extensions.Logging;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests;
using Shared.Models.Responses;
using Shared.Validation;
using Shared.Models.Requests.Auth;
using Shared.Models.Requests.Heartbeat;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Connections;
using Server.Application.Abstractions.Sfu;
using Server.Application.Coordinator.Heartbeat;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Connections.RateLimiting;
using Server.Application.Coordinator.Roles;

namespace Server.Application.Coordinator.Messaging;

public class MessageRouter : IMessageRouter
{
    public event Action<string, string>? MessageReceived;
    public event Action<string, ICoordinatorInstanceGateway>? NewConnectionOpened;
     
    private readonly Dictionary<Type, IMessageHandler> _handlers = new();
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger _logger;
    private const int MaxRejectionDetailsLength = 500;

    private readonly AuthenticationAttemptLimiter _authenticationLimiter;
    private readonly RequestAuthorizer? _authorizer;

    public MessageRouter(
        ILogger logger, 
        IEnumerable<IMessageHandler> handlers, 
        IConnectionManager connectionManager,
        RequestRateLimits? limits = null,
        RequestAuthorizer? authorizer = null)
    {
        _logger = logger;
        _authorizer = authorizer;
        _authenticationLimiter = new AuthenticationAttemptLimiter(TimeProvider.System, limits);
        
        _connectionManager = connectionManager;
        
        foreach (var handler in handlers)
        {
            _handlers.Add(handler.PayloadType, handler); 
        }
    }

    public void AddConnection(ICoordinatorInstanceGateway clientGateway)
    {
        _connectionManager.AddConnection(clientGateway);
        _logger.LogTrace(
            "Web socket client connection added with Id {ConnectionId}.",
            clientGateway.ConnectionId);
    }

    public void RouteIncomingMessage(BaseMessage? message, string connectionId)
    {
        if (message == null) 
            return;

        var messageType = message.GetType();

        if (!_connectionManager.TryGetConnectionById(connectionId, out var connection))
            return;

        var verdict = connection.RequestGuard.Check(message, out var group);
        if (verdict == RequestGuardVerdict.Allowed
            && group == RequestRateGroup.Authentication
            && !_authenticationLimiter.TryAcquire(connection.IpEndpoint))
            verdict = connection.RequestGuard.RegisterRejection();

        if (verdict != RequestGuardVerdict.Allowed)
        {
            RejectRateLimited(connection, message, group, verdict);
            return;
        }
        
        bool isAuth = typeof(AuthRequest).IsAssignableFrom(messageType);
        bool isHeartbeat = typeof(HeartbeatRequest).IsAssignableFrom(messageType);
            
        if (message.SenderId == 0 && !isAuth && !isHeartbeat)
        {
            _logger.LogWarning("Rejected message from {connectionId}. Attempt of perform action without auth", connectionId);
            connection.Close();
            return;
        }

        if (message is Request invalidCandidate && !MessageValidator.TryValidate(invalidCandidate, out var errors))
        {
            RejectInvalid(connection, invalidCandidate, errors);
            return;
        }
        
        if (_handlers.TryGetValue(messageType, out var handler))
        {
            // A ping is answered at once, so a long request does not make the connection look dead
            if (isHeartbeat)
                _ = DispatchAsync(handler, message, connection);
            else
                connection.HandlingQueue.Enqueue(() => DispatchAsync(handler, message, connection));
            return;
        }
        
        _logger.LogWarning("Handler not found for Type {Type}. Connection Id: {ConnectionId}", 
            messageType, connectionId);
    }

    public void RejectMalformedMessage(string connectionId)
    {
        if (!_connectionManager.TryGetConnectionById(connectionId, out var connection))
            return;

        if (connection.RequestGuard.RegisterRejection() != RequestGuardVerdict.RejectedTooOften)
            return;

        _logger.LogWarning("Connection {ConnectionId} from {Ip} sent malformed messages too often. Closing it.",
            connectionId, connection.IpEndpoint);
        connection.Close(ConnectionCloseReason.TooManyRejectedRequests);
    }

    private async Task DispatchAsync(IMessageHandler handler, BaseMessage message, IClientConnection connection)
    {
        try
        {
            if (_authorizer != null && _authorizer.IsFrozenOut(message))
            {
                RejectForbidden(connection, message, RequestRejectReason.UserFrozen);
                return;
            }

            if (_authorizer != null && !await _authorizer.IsAllowedAsync(message))
            {
                RejectForbidden(connection, message, RequestRejectReason.InsufficientPermissions);
                return;
            }

            await handler.HandleMessage(message, connection.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handling {MessageType} from connection {ConnectionId} (user {UserId}) failed.",
                message.GetType().Name, connection.ConnectionId, connection.UserId);
            SendFailureResponse(handler, message, connection);
        }
    }

    private void SendFailureResponse(IMessageHandler handler, BaseMessage message, IClientConnection connection)
    {
        try
        {
            var response = handler.CreateFailureResponse(message);
            if (response != null)
                connection.Send(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sending the failure response for {MessageType} to connection {ConnectionId} failed.",
                message.GetType().Name, connection.ConnectionId);
        }
    }

    private void RejectForbidden(IClientConnection connection, BaseMessage message, RequestRejectReason reason)
    {
        _logger.LogDebug("Rejected {MessageType} from user {UserId}: {Reason}.",
            message.GetType().Name, connection.UserId, reason);

        if (message is Request request)
            connection.Send(new RequestRejectedResponse(request.RequestId, reason));
    }

    private void RejectInvalid(IClientConnection connection, Request request, IReadOnlyList<string> errors)
    {
        var details = string.Join("; ", errors);
        if (details.Length > MaxRejectionDetailsLength)
            details = details[..MaxRejectionDetailsLength];

        _logger.LogDebug("Rejected invalid {MessageType} from connection {ConnectionId}: {Errors}",
            request.GetType().Name, connection.ConnectionId, details);

        connection.Send(new RequestRejectedResponse(request.RequestId, RequestRejectReason.InvalidRequest, details));

        if (connection.RequestGuard.RegisterRejection() != RequestGuardVerdict.RejectedTooOften)
            return;

        _logger.LogWarning("Connection {ConnectionId} from {Ip} sent invalid requests too often. Closing it.",
            connection.ConnectionId, connection.IpEndpoint);
        connection.Close(ConnectionCloseReason.TooManyRejectedRequests);
    }

    private void RejectRateLimited(IClientConnection connection, BaseMessage message, RequestRateGroup group,
        RequestGuardVerdict verdict)
    {
        _logger.LogDebug("Rate limited {MessageType} ({Group}) from connection {ConnectionId}.",
            message.GetType().Name, group, connection.ConnectionId);

        if (message is Request request)
            connection.Send(new RequestRejectedResponse(request.RequestId, RequestRejectReason.RateLimited, group.ToString()));

        if (verdict != RequestGuardVerdict.RejectedTooOften)
            return;

        _logger.LogWarning("Connection {ConnectionId} from {Ip} exceeded rate limits too often. Closing it.",
            connection.ConnectionId, connection.IpEndpoint);
        connection.Close(ConnectionCloseReason.TooManyRejectedRequests);
    }
}