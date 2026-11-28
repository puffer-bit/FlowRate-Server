using Shared.Enums;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Models;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Roles;

namespace Server.Application.Coordinator.Handlers.Chat;

public class DeleteTextMessageHandler : IMessageHandler<DeleteMessageRequest>
{
    private readonly IChatSessionManager _chatSessionManager;
    private readonly IMessageSender _messageSender;
    private readonly ICoordinatorPermissions? _permissions;

    public DeleteTextMessageHandler(IChatSessionManager chatSessionManager, IMessageSender messageSender,
        ICoordinatorPermissions? permissions = null)
    {
        _chatSessionManager = chatSessionManager;
        _messageSender = messageSender;
        _permissions = permissions;
    }

    public async Task<HandleMessageResult> HandleMessage(DeleteMessageRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;

        // ManageMessages counts as the chat overrides it
        var mayDeleteAny = _permissions != null && _chatSessionManager.TryGetSession(request.ChatId, out var chat)
            && _permissions.HasIn(request.SenderId, chat, Permission.ManageMessages);
        var result = await _chatSessionManager.DeleteMessageAsync(request.ChatId, request.MessageId, request.SenderId,
            mayDeleteAny);
        var responseData = request.Answer(result, request.ToResponse);
        
        _messageSender.SendMessageById(connectionId, 
            responseData);
        
        return result == null ? HandleMessageResult.NoError : HandleMessageResult.InternalError;
    }
}