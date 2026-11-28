using Shared.Enums;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Models;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Users;
using Server.Application.Mappers;

namespace Server.Application.Coordinator.Handlers.Chat;

public class SendMessageHandler : IMessageHandler<SendMessageRequest>
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

    public async Task<HandleMessageResult> HandleMessage(SendMessageRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;

        // The sender is checked in memory: a message from a user who is not connected is not accepted
        var (result, stored) = _userManager.TryGetConnectedUser(request.SenderId, out _)
            ? await _chatSessionManager.AddMessageWithResultAsync(request.ChatId, request.Message, request.SenderId)
            : (ErrorCode.NotConnected, null);
        var responseData = request.Answer(result, () => request.ToResponse(stored?.ToDto()));
        
        _messageSender.SendMessageById(connectionId, 
            responseData);
        
        return result == null ? HandleMessageResult.NoError : HandleMessageResult.InternalError;
    }
}