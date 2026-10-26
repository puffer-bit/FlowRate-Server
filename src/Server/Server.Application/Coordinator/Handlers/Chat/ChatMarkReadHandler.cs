using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Mappers;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.SessionActions.ChatSession;

namespace Server.Application.Coordinator.Handlers.Chat;

public class ChatMarkReadHandler : IMessageHandler<ChatMarkReadRequest>
{
    private readonly ChatReadService _readService;
    private readonly IMessageSender _messageSender;

    public ChatMarkReadHandler(ChatReadService readService, IMessageSender messageSender)
    {
        _readService = readService;
        _messageSender = messageSender;
    }

    public async Task<HandleMessageResult> HandleMessage(ChatMarkReadRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;

        var (result, state) = await _readService.MarkReadAsync(request.SenderId, request.ChatId, request.MessageId);
        _messageSender.SendMessageById(connectionId, request.ToResponse(result, state?.ToDto()));

        return result == ChatReadResult.NoError ? HandleMessageResult.NoError : HandleMessageResult.InternalError;
    }

    public BaseMessage? CreateFailureResponse(ChatMarkReadRequest request) =>
        request.ToResponse(ChatReadResult.InternalError, null);
}
