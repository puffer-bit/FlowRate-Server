using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Chat;
using Shared.Models;
using Shared.Models.Requests.SessionActions.ChatSession;

namespace Server.Application.Coordinator.Handlers.Chat;

public class SetMessageReactionHandler(IChatSessionManager chats, IMessageSender sender)
    : IMessageHandler<SetMessageReactionRequest>
{
    public async Task<HandleMessageResult> HandleMessage(SetMessageReactionRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var result = await chats.SetReactionAsync(request.ChatId, request.MessageId, request.Emoji,
            request.IsAdded, request.SenderId);
        sender.SendMessageById(connectionId, request.Answer(result, request.ToResponse));
        return HandleMessageResult.NoError;
    }
}
