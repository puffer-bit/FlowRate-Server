using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Messaging;
using Shared.Models.Requests.Auth;

namespace Server.Application.Coordinator.Handlers.Authentication;

/// <summary>Gives the connection the challenge a device or the recovery key signs to log in.</summary>
public class LoginChallengeHandler(LoginReplies replies, LoginNonces nonces, string coordinatorInstanceId)
    : IMessageHandler<LoginChallengeRequest>
{
    public Task<HandleMessageResult> HandleMessage(LoginChallengeRequest? request, string connectionId)
    {
        if (request == null || replies.LoggedOutConnection(request, connectionId) == null)
            return Task.FromResult(HandleMessageResult.ForbiddenRequest);

        replies.Send(connectionId, request.ToResponse(nonces.Issue(connectionId), coordinatorInstanceId));
        return Task.FromResult(HandleMessageResult.NoError);
    }
}
