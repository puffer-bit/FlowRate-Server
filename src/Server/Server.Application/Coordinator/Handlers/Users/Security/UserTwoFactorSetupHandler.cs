using Server.Application.Coordinator.Messaging;
using Server.Application.Mappers;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Requests.Coordinator.Security;

namespace Server.Application.Coordinator.Handlers.Users.Security;

/// <summary>Offers the sender a new one-time code secret.</summary>
public class UserTwoFactorSetupHandler(IMessageSender messageSender, UserSecurityReplies replies)
    : IMessageHandler<UserTwoFactorSetupRequest>
{
    public async Task<HandleMessageResult> HandleMessage(UserTwoFactorSetupRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        if (!await replies.IsProvenAsync(request.SenderId, request.Proof))
        {
            messageSender.SendMessageById(connectionId, request.ToError(ErrorCode.ProofRequired));
            return HandleMessageResult.ForbiddenRequest;
        }

        var (status, offer) = await replies.Security.OfferTwoFactorAsync(replies.CoordinatorId, request.SenderId);
        messageSender.SendMessageById(connectionId, offer == null
            ? request.ToError(status.ToResult() ?? ErrorCode.Internal)
            : request.ToResponse(offer.Secret, offer.Uri));
        return HandleMessageResult.NoError;
    }
}
