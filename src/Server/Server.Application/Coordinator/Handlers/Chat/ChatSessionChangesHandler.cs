using Shared.Enums;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Models;
using Server.Application.Mappers;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Chat;

namespace Server.Application.Coordinator.Handlers.Chat;

public class ChatSessionChangesHandler : IMessageHandler<ChatSessionChangesRequest>
{
    private readonly IChatSessionManager _chatSessionManager;
    private readonly IMessageSender _messageSender;

    public ChatSessionChangesHandler(IChatSessionManager chatSessionManager, IMessageSender messageSender)
    {
        _chatSessionManager = chatSessionManager;
        _messageSender = messageSender;
    }

    public async Task<HandleMessageResult> HandleMessage(ChatSessionChangesRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;

        var (result, changes) = await _chatSessionManager.FetchChangesAsync(request.ChatId,
            request.AfterRevision, request.Take);

        var responseData = changes != null
            ? request.ToResponse(result, changes.Messages.ToDtoList(), changes.Revision, changes.HasMore, changes.Reset)
            : request.ToResponse(result, null, 0, false, false);

        _messageSender.SendMessageById(connectionId, responseData);

        return result == FetchMessagesResult.NoError ? HandleMessageResult.NoError : HandleMessageResult.InternalError;
    }

    public BaseMessage? CreateFailureResponse(ChatSessionChangesRequest request) =>
        request.ToResponse(FetchMessagesResult.InternalError, null, 0, false, false);
}
