using Shared.Models;
using Shared.Models.Requests.SessionActions.Generic;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Chat;

namespace Server.Application.Coordinator.Handlers.Sessions;

public class ChangeChatSessionSettingsHandler : IMessageHandler<ChangeChatSessionSettingsRequest>
{
    private readonly IChatSessionManager _sessionManager;
    private readonly IMessageSender _messageSender;

    public ChangeChatSessionSettingsHandler(IChatSessionManager sessionManager, IMessageSender messageSender)
    {
        _sessionManager = sessionManager;
        _messageSender = messageSender;
    }

    public async Task<HandleMessageResult> HandleMessage(ChangeChatSessionSettingsRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var result = await _sessionManager.ChangeSettingsAsync(request.SessionId, request.Name.Trim(),
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(), request.SenderId);
        _messageSender.SendMessageById(connectionId, request.Answer(result, request.ToResponse));

        return HandleMessageResult.NoError;
    }
}
