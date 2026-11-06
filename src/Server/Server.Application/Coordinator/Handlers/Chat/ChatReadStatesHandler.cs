using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Mappers;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.SessionActions.ChatSession;

namespace Server.Application.Coordinator.Handlers.Chat;

public class ChatReadStatesHandler : IMessageHandler<ChatReadStatesRequest>
{
    private readonly ChatReadService _readService;
    private readonly IMessageSender _messageSender;

    public ChatReadStatesHandler(ChatReadService readService, IMessageSender messageSender)
    {
        _readService = readService;
        _messageSender = messageSender;
    }

    public async Task<HandleMessageResult> HandleMessage(ChatReadStatesRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;

        var states = await _readService.GetStatesAsync(request.SenderId);
        _messageSender.SendMessageById(connectionId,
            request.ToResponse(states.Select(state => state.ToDto()).ToList()));

        return HandleMessageResult.NoError;
    }
}
