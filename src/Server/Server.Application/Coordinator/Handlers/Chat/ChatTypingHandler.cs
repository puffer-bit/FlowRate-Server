using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Users;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Notifications.SessionInfo;
using Shared.Models.Requests.SessionActions.ChatSession;

namespace Server.Application.Coordinator.Handlers.Chat;

public sealed class ChatTypingHandler(IChatSessionManager chats, IUserManager users,
    SessionVisibility visibility, IMessageSender sender) : IMessageHandler<ChatTypingRequest>
{
    public Task<HandleMessageResult> HandleMessage(ChatTypingRequest? request, string connectionId)
    {
        if (request == null)
            return Task.FromResult(HandleMessageResult.ForbiddenRequest);

        ErrorCode? error = !users.TryGetConnectedUser(request.SenderId, out _) ? ErrorCode.NotConnected
            : !chats.TryGetSession(request.ChatId, out _) ? ErrorCode.ChatNotFound : null;
        if (error == null)
            visibility.Send(request.ChatId,
                new ChatSessionUserTypingNotification(request.ChatId, request.SenderId, request.IsTyping));

        sender.SendMessageById(connectionId, request.Answer(error, request.ToResponse));
        return Task.FromResult(error == null ? HandleMessageResult.NoError : HandleMessageResult.InternalError);
    }
}
