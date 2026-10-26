using Server.Application.Coordinator.Messaging;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Requests.Coordinator.Security;

namespace Server.Application.Coordinator.Handlers.Users.Security;

/// <summary>Turns one-time codes on for the sender.</summary>
public class UserTwoFactorEnableHandler(IMessageSender messageSender, UserSecurityReplies replies)
    : IMessageHandler<UserTwoFactorEnableRequest>
{
    public async Task<HandleMessageResult> HandleMessage(UserTwoFactorEnableRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        // Proven when the secret was offered; the code proves the authenticator app has it
        var status = await replies.Security.EnableTwoFactorAsync(replies.CoordinatorId, request.SenderId,
            request.Code);
        var (result, security) = await replies.AfterAsync(request.SenderId, status);
        messageSender.SendMessageById(connectionId, request.ToResponse(result, security));
        return HandleMessageResult.NoError;
    }

    public BaseMessage? CreateFailureResponse(UserTwoFactorEnableRequest request) =>
        request.ToResponse(UserSecurityResult.InternalError);
}
