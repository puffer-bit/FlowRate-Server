using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Messaging;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Requests.Coordinator.Security;

namespace Server.Application.Coordinator.Handlers.Users.Security;

/// <summary>Ends a session of the sender, or all of them but the current one, and disconnects their devices.</summary>
public class UserSessionRevokeHandler(IMessageSender messageSender, IConnectionManager connectionManager,
    UserSessions sessions) : IMessageHandler<UserSessionRevokeRequest>
{
    public async Task<HandleMessageResult> HandleMessage(UserSessionRevokeRequest? request, string connectionId)
    {
        if (request == null || !connectionManager.TryGetConnectionById(connectionId, out var connection))
            return HandleMessageResult.ForbiddenRequest;

        var current = connection.UserSessionId;
        if (request.SessionId is { } sessionId)
        {
            // The current session is left with a logout, not ended from under the connection that asks
            if (sessionId == current || !await sessions.EndAsync(request.SenderId, sessionId))
            {
                messageSender.SendMessageById(connectionId, request.ToError(ErrorCode.SessionNotFound));
                return HandleMessageResult.NoError;
            }
        }
        else
        {
            await sessions.EndAllButAsync(request.SenderId, current, UserSessionEndReason.Revoked);
        }

        var list = await sessions.GetAsync(request.SenderId, current);
        messageSender.SendMessageById(connectionId, request.ToResponse(list));
        return HandleMessageResult.NoError;
    }
}
