using Shared.Enums;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Models;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Authentication;

namespace Server.Application.Coordinator.Handlers.Chat;

public class EditTextMessageHandler : IMessageHandler<EditTextMessageRequest>
{
    private readonly IChatSessionManager _chatSessionManager;
    private readonly IMessageSender _messageSender;

    public EditTextMessageHandler(IChatSessionManager chatSessionManager, IMessageSender messageSender)
    {
        _chatSessionManager = chatSessionManager;
        _messageSender = messageSender;
    }

    public async Task<HandleMessageResult> HandleMessage(EditTextMessageRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;

        var result = await _chatSessionManager.EditMessageAsync(request.ChatId, request.MessageId, request.Text, request.SenderId);
        var responseData = request.ToResponse(result);
        
        _messageSender.SendMessageById(connectionId, 
            responseData);
        
        return result == EditMessageResult.NoError ? HandleMessageResult.NoError : HandleMessageResult.InternalError;
    }

    public BaseMessage? CreateFailureResponse(EditTextMessageRequest request) =>
        request.ToResponse(EditMessageResult.InternalError);
}