using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Chat;
using Shared.Models;
using Shared.Models.DataTransferObjects.ChatSession;
using Shared.Models.Requests.SessionActions.ChatSession;

namespace Server.Application.Coordinator.Handlers.Chat;

public class ChatMessageReadersHandler(ChatReadService readService, IMessageSender sender)
    : IMessageHandler<ChatMessageReadersRequest>
{
    public async Task<HandleMessageResult> HandleMessage(ChatMessageReadersRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;

        var (result, readers) = await readService.GetReadersAsync(request.SenderId, request.ChatId, request.MessageId);
        sender.SendMessageById(connectionId, request.Answer(result, () => request.ToResponse(
            readers!.Select(reader => new ChatMessageReaderDTO(reader.UserId, reader.ReadAt)).ToList())));
        return HandleMessageResult.NoError;
    }
}
