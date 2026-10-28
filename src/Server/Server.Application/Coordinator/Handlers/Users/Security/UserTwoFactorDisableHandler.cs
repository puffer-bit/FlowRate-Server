using Server.Application.Coordinator.Messaging;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Requests.Coordinator.Security;

namespace Server.Application.Coordinator.Handlers.Users.Security;

/// <summary>Turns one-time codes off for the sender.</summary>
public class UserTwoFactorDisableHandler(IMessageSender messageSender, UserSecurityReplies replies)
    : IMessageHandler<UserTwoFactorDisableRequest>
{
    public async Task<HandleMessageResult> HandleMessage(UserTwoFactorDisableRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        if (!await replies.IsProvenAsync(request.SenderId, request.Proof))
        {
            messageSender.SendMessageById(connectionId, request.ToError(ErrorCode.ProofRequired));
            return HandleMessageResult.ForbiddenRequest;
        }

        var status = await replies.Security.DisableTwoFactorAsync(replies.CoordinatorId, request.SenderId);
        var (result, security) = await replies.AfterAsync(request.SenderId, status);
        messageSender.SendMessageById(connectionId, request.Answer(result, () => request.ToResponse(security!)));
        return HandleMessageResult.NoError;
    }
}
