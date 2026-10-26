using Shared.Enums;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Models;
using Server.Application.Mappers;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Chat;

namespace Server.Application.Coordinator.Handlers.Chat;

public class ChatSessionHistoryPageHandler : IMessageHandler<ChatSessionHistoryPageRequest>
{
    private readonly IChatSessionManager _chatSessionManager;
    private readonly IMessageSender _messageSender;

    public ChatSessionHistoryPageHandler(IChatSessionManager chatSessionManager, IMessageSender messageSender)
    {
        _chatSessionManager = chatSessionManager;
        _messageSender = messageSender;
    }

    public async Task<HandleMessageResult> HandleMessage(ChatSessionHistoryPageRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;

        var (result, page) = await _chatSessionManager.FetchMessagesBeforeAsync(request.ChatId,
            request.BeforeMessageId, request.Take);

        var responseData = page != null
            ? request.ToResponse(result, page.Messages.ToDtoList(), page.Revision)
            : request.ToResponse(result, null, 0);

        _messageSender.SendMessageById(connectionId, responseData);

        return result == FetchMessagesResult.NoError ? HandleMessageResult.NoError : HandleMessageResult.InternalError;
    }

    public BaseMessage? CreateFailureResponse(ChatSessionHistoryPageRequest request) =>
        request.ToResponse(FetchMessagesResult.InternalError, null, 0);
}
