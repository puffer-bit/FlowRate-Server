using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Moderation;
using Server.Application.Coordinator.Users;
using Server.Application.Mappers;
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

public class BanHandler(IMessageSender sender, IUserManager users, ModerationService moderation)
    : IMessageHandler<CoordinatorBanRequest>
{
    public async Task<HandleMessageResult> HandleMessage(CoordinatorBanRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;
        var (error, ban) = await moderation.BanAsync(request.SenderId, request);
        var user = error == null ? (await users.GetUserAsync(request.TargetUserId))?.ToDto(request.SenderId) : null;
        if (error == null && user == null) error = ErrorCode.UserNotFound;
        sender.SendMessageById(connectionId, request.Answer(error, () => request.ToResponse(ban!.ToDto(), user!)));
        return HandleMessageResult.NoError;
    }
}
