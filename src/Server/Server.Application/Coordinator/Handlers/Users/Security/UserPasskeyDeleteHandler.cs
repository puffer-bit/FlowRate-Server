using Server.Application.Coordinator.Messaging;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Requests.Coordinator.Security;

namespace Server.Application.Coordinator.Handlers.Users.Security;

/// <summary>Removes a passkey of the sender.</summary>
public class UserPasskeyDeleteHandler(IMessageSender messageSender, UserSecurityReplies replies)
    : IMessageHandler<UserPasskeyDeleteRequest>
{
    public async Task<HandleMessageResult> HandleMessage(UserPasskeyDeleteRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        if (!await replies.IsProvenAsync(request.SenderId, request.Proof))
        {
            messageSender.SendMessageById(connectionId, request.ToResponse(UserSecurityResult.ProofRequired));
            return HandleMessageResult.ForbiddenRequest;
        }

        var status = await replies.Security.DeletePasskeyAsync(replies.CoordinatorId, request.SenderId,
            request.PasskeyId);
        var (result, security) = await replies.AfterAsync(request.SenderId, status);
        messageSender.SendMessageById(connectionId, request.ToResponse(result, security));
        return HandleMessageResult.NoError;
    }

    public BaseMessage? CreateFailureResponse(UserPasskeyDeleteRequest request) =>
        request.ToResponse(UserSecurityResult.InternalError);
}
