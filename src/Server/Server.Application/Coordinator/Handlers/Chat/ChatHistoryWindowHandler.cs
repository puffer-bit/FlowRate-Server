using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Mappers;
using Shared.Models;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Models.Responses.SessionActions.ChatSession;

namespace Server.Application.Coordinator.Handlers.Chat;

public class ChatHistoryWindowHandler(IChatSessionManager chats, IMessageSender sender) : IMessageHandler<ChatHistoryWindowRequest>
{
    public async Task<HandleMessageResult> HandleMessage(ChatHistoryWindowRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;
        var (result, page) = await chats.FetchWindowAsync(request.ChatId, request.MessageId, request.Direction, request.Take);
        sender.SendMessageById(connectionId, request.Answer(result,
            () => new ChatHistoryWindowResponse(request.RequestId, page!.Messages.ToDtoList(), page.Revision, page.HasOlder, page.HasNewer)));
        return HandleMessageResult.NoError;
    }
}
