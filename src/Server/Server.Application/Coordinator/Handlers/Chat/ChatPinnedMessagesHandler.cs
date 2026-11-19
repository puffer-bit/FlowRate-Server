using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Mappers;
using Shared.Models;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Models.Responses.SessionActions.ChatSession;

namespace Server.Application.Coordinator.Handlers.Chat;

public class ChatPinnedMessagesHandler(IChatSessionManager chats, IMessageSender sender) : IMessageHandler<ChatPinnedMessagesRequest>
{
    public async Task<HandleMessageResult> HandleMessage(ChatPinnedMessagesRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;
        var (result, page) = await chats.FetchPinnedAsync(request.ChatId);
        sender.SendMessageById(connectionId, request.Answer(result,
            () => new ChatPinnedMessagesResponse(request.RequestId, page!.Messages.ToDtoList(), page.Revision)));
        return HandleMessageResult.NoError;
    }
}
