using Shared.Enums;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Models;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Users;

namespace Server.Application.Coordinator.Handlers.Chat;

public class SendMessageHandler : IMessageHandler<SendTextMessageRequest>
{
    private readonly IChatSessionManager _chatSessionManager;
    private readonly IMessageSender _messageSender;
    private readonly IUserManager _userManager;

    public SendMessageHandler(IChatSessionManager chatSessionManager, IMessageSender messageSender,
        IUserManager userManager)
    {
        _chatSessionManager = chatSessionManager;
        _messageSender = messageSender;
        _userManager = userManager;
    }

    public async Task<HandleMessageResult> HandleMessage(SendTextMessageRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;

        // The sender is checked in memory: a message from a user who is not connected is not accepted
        var result = _userManager.TryGetConnectedUser(request.SenderId, out _)
            ? await _chatSessionManager.AddMessageAsync(request.ChatId, request.MessageModel, request.SenderId)
            : AddMessageResult.Rejected;
        var responseData = request.ToResponse(result);
        
        _messageSender.SendMessageById(connectionId, 
            responseData);
        
        return result == AddMessageResult.NoError ? HandleMessageResult.NoError : HandleMessageResult.InternalError;
    }

    public BaseMessage? CreateFailureResponse(SendTextMessageRequest request) =>
        request.ToResponse(AddMessageResult.InternalError);
}