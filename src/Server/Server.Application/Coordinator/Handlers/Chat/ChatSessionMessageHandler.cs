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

public class ChatSessionMessageHandler : IMessageHandler<ChatSessionMessageRequest>
{

    private readonly IChatSessionManager _chatSessionManager;
    private readonly IMessageSender _messageSender;

    public ChatSessionMessageHandler(IChatSessionManager chatSessionManager, IMessageSender messageSender)
    {
        _chatSessionManager = chatSessionManager;
        _messageSender = messageSender;
    }

    public async Task<HandleMessageResult> HandleMessage(ChatSessionMessageRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;

        var result = await _chatSessionManager.FetchMessageAsync(request.ChatId, request.MessageId);
        
        var responseData = result.result == FetchMessageResult.NoError 
            ? request.ToResponse(result.result, result.message.ToDto()) 
            : request.ToResponse(result.result, null);

        _messageSender.SendMessageById(connectionId, 
            responseData);
        
        return result.result == FetchMessageResult.NoError ? HandleMessageResult.NoError : HandleMessageResult.InternalError;
    }

    public BaseMessage? CreateFailureResponse(ChatSessionMessageRequest request) =>
        request.ToResponse(FetchMessageResult.InternalError, null);
}