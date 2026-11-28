using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Messaging;
using Shared.Enums;
using Shared.Models.Requests.Coordinator.Security;

namespace Server.Application.Coordinator.Handlers.Users.Security;

/// <summary>
/// Gives the sender the recovery keys of a new phrase: the old phrase stops working. The devices stay, they have
/// keys of their own.
/// </summary>
public class UserRecoveryKeyResetHandler(IMessageSender messageSender, UserSecurityReplies replies,
    IUserRepository users) : IMessageHandler<UserRecoveryKeyResetRequest>
{
    public async Task<HandleMessageResult> HandleMessage(UserRecoveryKeyResetRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        if (!await replies.IsProvenAsync(request.SenderId, request.Proof))
        {
            messageSender.SendMessageById(connectionId, request.ToError(ErrorCode.ProofRequired));
            return HandleMessageResult.ForbiddenRequest;
        }

        if (RecoveryKeys.From(request.Keys) is not { } keys
            || await users.IsRecoveryKeyTakenAsync(keys.RecoveryKeyHash, request.SenderId))
        {
            messageSender.SendMessageById(connectionId, request.ToError(ErrorCode.InvalidValue));
            return HandleMessageResult.NoError;
        }

        await users.SetRecoveryKeysAsync(request.SenderId, keys);
        messageSender.SendMessageById(connectionId, request.ToResponse());
        return HandleMessageResult.NoError;
    }
}
