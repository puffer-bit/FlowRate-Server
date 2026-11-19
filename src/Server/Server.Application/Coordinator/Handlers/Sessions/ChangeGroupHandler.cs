using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Users;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.SessionActions.Generic;

namespace Server.Application.Coordinator.Handlers.Sessions;

public sealed class ChangeGroupHandler(IChatSessionManager chats, IUserManager users, IMessageSender sender)
    : IMessageHandler<ChangeGroupRequest>
{
    public async Task<HandleMessageResult> HandleMessage(ChangeGroupRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;
        if (!chats.TryGetSession(request.SessionId, out var group))
        {
            sender.SendMessageById(connectionId, request.ToError(ErrorCode.ChatNotFound));
            return HandleMessageResult.NoError;
        }
        ErrorCode? result = null;
        if (!request.Leave)
            foreach (var id in request.MemberIds)
            {
                var user = await users.GetUserAsync(id);
                if (user == null || user.IsSystem || user.CoordinatorInstanceId != group.CoordinatorInstanceId)
                {
                    result = ErrorCode.InvalidValue;
                    break;
                }
            }
        result ??= await chats.ChangeGroupAsync(request.SessionId, request.Name.Trim(), request.MemberIds,
            request.OwnerId, request.SenderId, request.Leave);
        sender.SendMessageById(connectionId, request.Answer(result, request.ToResponse));
        return HandleMessageResult.NoError;
    }
}
