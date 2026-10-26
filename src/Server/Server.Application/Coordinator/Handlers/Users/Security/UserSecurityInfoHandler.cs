using Server.Application.Coordinator.Messaging;
using Server.Application.Mappers;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Requests.Coordinator.Security;

namespace Server.Application.Coordinator.Handlers.Users.Security;

/// <summary>What the sender logs in with.</summary>
public class UserSecurityInfoHandler(IMessageSender messageSender, UserSecurityReplies replies)
    : IMessageHandler<UserSecurityInfoRequest>
{
    public async Task<HandleMessageResult> HandleMessage(UserSecurityInfoRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var overview = await replies.Security.GetAsync(replies.CoordinatorId, request.SenderId);
        messageSender.SendMessageById(connectionId, overview == null
            ? request.ToResponse(UserSecurityResult.InternalError)
            : request.ToResponse(UserSecurityResult.NoError, overview.ToDto()));
        return HandleMessageResult.NoError;
    }

    public BaseMessage? CreateFailureResponse(UserSecurityInfoRequest request) =>
        request.ToResponse(UserSecurityResult.InternalError);
}
