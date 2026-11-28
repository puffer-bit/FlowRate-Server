using Server.Application.Coordinator.Media;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Users;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.SessionActions.Generic;

namespace Server.Application.Coordinator.Handlers.Sessions;

public sealed class ChangeGroupHandler(IChatSessionManager chats, IUserManager users, IMessageSender sender)
    : IMessageHandler<ChangeGroupRequest>
{
    public async Task<HandleMessageResult> HandleMessage(ChangeGroupRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;
        if (!chats.TryGetSession(request.SessionId, out var group))
        {
            sender.SendMessageById(connectionId, request.ToError(ErrorCode.ChatNotFound));
            return HandleMessageResult.NoError;
        }
        ErrorCode? result = null;
        foreach (var id in request.MemberIds)
        {
            var user = await users.GetUserAsync(id);
            if (user == null || user.IsSystem || user.CoordinatorInstanceId != group.CoordinatorInstanceId)
            {
                result = ErrorCode.InvalidValue;
                break;
            }
        }
        result ??= await chats.ChangeGroupAsync(request.SessionId, request.Name.Trim(), request.MemberIds,
            request.OwnerUserId, request.SenderId, leave: false);
        sender.SendMessageById(connectionId, request.Answer(result, request.ToResponse));
        return HandleMessageResult.NoError;
    }
}

public sealed class LeaveGroupHandler(IChatSessionManager chats, IMessageSender sender)
    : IMessageHandler<LeaveGroupRequest>
{
    public async Task<HandleMessageResult> HandleMessage(LeaveGroupRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;
        var result = await chats.ChangeGroupAsync(request.SessionId, string.Empty, [], 0, request.SenderId, leave: true);
        sender.SendMessageById(connectionId, request.Answer(result, request.ToResponse));
        return HandleMessageResult.NoError;
    }
}

/// <summary>
/// The owner puts an avatar upload of theirs on a group, or takes the picture off. The replaced picture is removed.
/// </summary>
public sealed class SetGroupAvatarHandler(IChatSessionManager chats, IMediaService media, IMessageSender sender)
    : IMessageHandler<SetGroupAvatarRequest>
{
    public async Task<HandleMessageResult> HandleMessage(SetGroupAvatarRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;
        sender.SendMessageById(connectionId, request.Answer(await SetAvatarAsync(request), request.ToResponse));
        return HandleMessageResult.NoError;
    }

    private async Task<ErrorCode?> SetAvatarAsync(SetGroupAvatarRequest request)
    {
        if (!chats.TryGetSession(request.SessionId, out var group) || group.Kind != ChatKind.Group)
            return ErrorCode.ChatNotFound;
        if (group.OwnerUserId != request.SenderId)
            return ErrorCode.InsufficientPermissions;

        var uploadId = request.UploadId?.ToLowerInvariant();
        if (uploadId != null && !await media.AttachAvatarAsync(uploadId, request.SenderId))
            return ErrorCode.UploadNotFound;

        var (result, previous) = await chats.SetGroupAvatarAsync(request.SessionId,
            uploadId == null ? null : AvatarAddress.Of(uploadId), request.SenderId);
        if (result != null)
        {
            if (uploadId != null)
                await media.RemoveAvatarAsync(uploadId);
            return result;
        }

        if (AvatarAddress.UploadIdOf(previous) is { } previousId && previousId != uploadId)
            await media.RemoveAvatarAsync(previousId);
        return null;
    }
}
