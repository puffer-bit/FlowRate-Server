using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.SessionActions.Generic;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Users;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Authentication;

namespace Server.Application.Coordinator.Handlers.Sessions;

public class DeleteSessionHandler : IMessageHandler<DeleteSessionRequest>
{
    private readonly ISessionManager _sessionManager;
    private readonly IMessageSender _messageSender;

    public DeleteSessionHandler(ISessionManager sessionManager, IMessageSender messageSender)
    {
        _sessionManager = sessionManager;
        _messageSender = messageSender;
    }

    public async Task<HandleMessageResult> HandleMessage(DeleteSessionRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var result = await _sessionManager.DeleteSessionAsync(request.SessionId, request.SenderId);
        _messageSender.SendMessageById(connectionId, request.Answer(result, request.ToResponse));

        return HandleMessageResult.NoError;
    }
}
