using Server.Application.Coordinator.Messaging;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Requests.Coordinator.Security;

namespace Server.Application.Coordinator.Handlers.Users.Security;

/// <summary>Registers a passkey of the sender.</summary>
public class UserPasskeyRegisterHandler(IMessageSender messageSender, UserSecurityReplies replies)
    : IMessageHandler<UserPasskeyRegisterRequest>
{
    public async Task<HandleMessageResult> HandleMessage(UserPasskeyRegisterRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        // Proven when the options were given; the passkey has to answer them
        var status = await replies.Security.RegisterPasskeyAsync(replies.CoordinatorId, request.SenderId,
            request.Label, request.CredentialJson);
        var (result, security) = await replies.AfterAsync(request.SenderId, status);
        messageSender.SendMessageById(connectionId, request.ToResponse(result, security));
        return HandleMessageResult.NoError;
    }

    public BaseMessage? CreateFailureResponse(UserPasskeyRegisterRequest request) =>
        request.ToResponse(UserSecurityResult.InternalError);
}
