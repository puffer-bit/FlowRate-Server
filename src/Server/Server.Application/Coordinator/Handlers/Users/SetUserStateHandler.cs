using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Moderation;
using Server.Application.Coordinator.Users;
using Server.Application.Mappers;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.Coordinator;

namespace Server.Application.Coordinator.Handlers.Users;

public class SetUserStateHandler(IMessageSender sender, IUserManager users, ModerationService moderation)
    : IMessageHandler<CoordinatorSetUserStateRequest>
{
    public async Task<HandleMessageResult> HandleMessage(CoordinatorSetUserStateRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;
        var error = await moderation.SetStateAsync(request.SenderId, request);
        var user = error == null ? (await users.GetUserAsync(request.TargetUserId))?.ToDto(request.SenderId) : null;
        sender.SendMessageById(connectionId, request.Answer(error, () => request.ToResponse(user!)));
        return HandleMessageResult.NoError;
    }
}
