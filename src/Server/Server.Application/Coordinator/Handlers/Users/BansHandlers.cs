using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Moderation;
using Shared.Enums;
using Shared.Models.Requests.Coordinator;

namespace Server.Application.Coordinator.Handlers.Users;

public class BansHandler(IMessageSender sender, ModerationService moderation) : IMessageHandler<CoordinatorBansRequest>
{
    public async Task<HandleMessageResult> HandleMessage(CoordinatorBansRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;
        sender.SendMessageById(connectionId, request.ToResponse(await moderation.ListAsync()));
        return HandleMessageResult.NoError;
    }
}

public class RevokeBanHandler(IMessageSender sender, ModerationService moderation) : IMessageHandler<CoordinatorRevokeBanRequest>
{
    public async Task<HandleMessageResult> HandleMessage(CoordinatorRevokeBanRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;
        sender.SendMessageById(connectionId, request.Answer(await moderation.RevokeAsync(request.SenderId, request.BanId), request.ToResponse));
        return HandleMessageResult.NoError;
    }
}
