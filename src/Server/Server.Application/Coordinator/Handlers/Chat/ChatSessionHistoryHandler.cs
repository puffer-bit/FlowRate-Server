using Shared.Enums;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Models;
using Server.Application.Mappers;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Authentication;

namespace Server.Application.Coordinator.Handlers.Chat;

public class ChatSessionHistoryHandler : IMessageHandler<ChatSessionHistoryRequest>
{
    private readonly IChatSessionManager _chatSessionManager;
    private readonly IMessageSender _messageSender;

    public ChatSessionHistoryHandler(IChatSessionManager chatSessionManager, IMessageSender messageSender)
    {
        _chatSessionManager = chatSessionManager;
        _messageSender = messageSender;
    }

    public async Task<HandleMessageResult> HandleMessage(ChatSessionHistoryRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;

        var result = await _chatSessionManager.FetchMessagesAsync(request.ChatId, request.Take, request.Skip);
        
        var responseData = result.result == FetchMessagesResult.NoError 
            ? request.ToResponse(result.result, result.messages.ToDtoList()) 
            : request.ToResponse(result.result, null);

        _messageSender.SendMessageById(connectionId, 
            responseData);
        
        return result.result == FetchMessagesResult.NoError ? HandleMessageResult.NoError : HandleMessageResult.InternalError;
    }

    public BaseMessage? CreateFailureResponse(ChatSessionHistoryRequest request) =>
        request.ToResponse(FetchMessagesResult.InternalError, null);
}