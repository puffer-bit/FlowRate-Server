using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Hybrid;
using Shared.Models.Requests.SessionActions.ChatSession;

namespace Server.Application.Coordinator.Handlers.Chat;

public sealed class DirectCallHandler(IHybridSessionManager sessions, IMessageSender sender)
    : IMessageHandler<DirectCallRequest>
{
    public async Task<HandleMessageResult> HandleMessage(DirectCallRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;
        var (error, call) = await sessions.ChangeCallAsync(request, connectionId);
        sender.SendMessageById(connectionId, error is { } code ? request.ToError(code) : request.ToResponse(call!));
        return HandleMessageResult.NoError;
    }
}
