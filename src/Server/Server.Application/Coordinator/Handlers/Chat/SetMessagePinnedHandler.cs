using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Mappers;
using Shared.Models;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Models.Responses.SessionActions.ChatSession;

namespace Server.Application.Coordinator.Handlers.Chat;

public class SetMessagePinnedHandler(IChatSessionManager chats, IMessageSender sender) : IMessageHandler<SetMessagePinnedRequest>
{
    public async Task<HandleMessageResult> HandleMessage(SetMessagePinnedRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;
        var result = await chats.SetPinnedAsync(request.ChatId, request.MessageId, request.IsPinned, request.SenderId);
        sender.SendMessageById(connectionId, request.Answer(result, request.ToResponse));
        return HandleMessageResult.NoError;
    }
}
