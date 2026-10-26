using Server.Application.Coordinator.Messaging;
using Server.Application.Mappers;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Requests.Coordinator.Security;

namespace Server.Application.Coordinator.Handlers.Users.Security;

/// <summary>Options for a new passkey of the sender.</summary>
public class UserPasskeyOptionsHandler(IMessageSender messageSender, UserSecurityReplies replies)
    : IMessageHandler<UserPasskeyOptionsRequest>
{
    public async Task<HandleMessageResult> HandleMessage(UserPasskeyOptionsRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        if (!await replies.IsProvenAsync(request.SenderId, request.Proof))
        {
            messageSender.SendMessageById(connectionId, request.ToResponse(UserSecurityResult.ProofRequired));
            return HandleMessageResult.ForbiddenRequest;
        }

        var (status, options) = await replies.Security.BeginPasskeyRegistrationAsync(replies.CoordinatorId,
            request.SenderId);
        messageSender.SendMessageById(connectionId, options == null
            ? request.ToResponse(status.ToResult())
            : request.ToResponse(UserSecurityResult.NoError, options));
        return HandleMessageResult.NoError;
    }

    public BaseMessage? CreateFailureResponse(UserPasskeyOptionsRequest request) =>
        request.ToResponse(UserSecurityResult.InternalError);
}
