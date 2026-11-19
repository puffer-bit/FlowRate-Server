using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Users;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Requests.Coordinator.Security;

namespace Server.Application.Coordinator.Handlers.Users.Security;

/// <summary>
/// Gives the sender a new identity, and ends every other session: what was logged in with the old identity, or could
/// be, is logged out.
/// </summary>
public class UserIdentityResetHandler(IMessageSender messageSender, IConnectionManager connectionManager,
    UserSecurityReplies replies, IUserRepository users, UserSessions sessions)
    : IMessageHandler<UserIdentityResetRequest>
{
    public async Task<HandleMessageResult> HandleMessage(UserIdentityResetRequest? request, string connectionId)
    {
        if (request == null || !connectionManager.TryGetConnectionById(connectionId, out var connection))
            return HandleMessageResult.ForbiddenRequest;

        if (!await replies.IsProvenAsync(request.SenderId, request.Proof))
        {
            messageSender.SendMessageById(connectionId, request.ToError(ErrorCode.ProofRequired));
            return HandleMessageResult.ForbiddenRequest;
        }

        var identity = UserManager.NewIdentity();
        await users.SetIdentityAsync(request.SenderId, identity);
        await sessions.EndAllButAsync(request.SenderId, connection.UserSessionId, UserSessionEndReason.IdentityReset);

        messageSender.SendMessageById(connectionId, request.ToResponse(identity));
        return HandleMessageResult.NoError;
    }
}
