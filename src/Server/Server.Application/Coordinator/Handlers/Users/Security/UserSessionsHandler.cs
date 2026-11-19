using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Messaging;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Requests.Coordinator.Security;

namespace Server.Application.Coordinator.Handlers.Users.Security;

/// <summary>The devices the sender is logged in from.</summary>
public class UserSessionsHandler(IMessageSender messageSender, IConnectionManager connectionManager,
    UserSessions sessions) : IMessageHandler<UserSessionsRequest>
{
    public async Task<HandleMessageResult> HandleMessage(UserSessionsRequest? request, string connectionId)
    {
        if (request == null || !connectionManager.TryGetConnectionById(connectionId, out var connection))
            return HandleMessageResult.ForbiddenRequest;

        var list = await sessions.GetAsync(request.SenderId, connection.UserSessionId);
        messageSender.SendMessageById(connectionId, request.ToResponse(list));
        return HandleMessageResult.NoError;
    }
}
