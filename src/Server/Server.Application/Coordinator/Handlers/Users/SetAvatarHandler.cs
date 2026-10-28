using Microsoft.Extensions.Logging;
using Server.Application.Coordinator.Media;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Users;
using Server.Application.Mappers;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Requests.Coordinator;

namespace Server.Application.Coordinator.Handlers.Users;

public class SetAvatarHandler : IMessageHandler<CoordinatorSetAvatarRequest>
{
    private readonly ILogger _logger;

    private readonly IMessageSender _messageSender;
    private readonly IUserManager _userManager;
    private readonly IMediaService _media;

    public SetAvatarHandler(
        ILogger logger,
        IMessageSender messageSender,
        IUserManager userManager,
        IMediaService media)
    {
        _logger = logger;
        _messageSender = messageSender;
        _userManager = userManager;
        _media = media;
    }

    public async Task<HandleMessageResult> HandleMessage(CoordinatorSetAvatarRequest? request, string connectionId)
    {
        if (request == null)
        {
            return HandleMessageResult.ForbiddenRequest;
        }

        var userId = request.TargetUserId ?? request.SenderId;

        var result = await SetAvatar(request, userId);
        var user = result == null && _userManager.TryGetConnectedUser(userId, out var updated)
            ? updated!.ToDto(request.SenderId)
            : null;

        _messageSender.SendMessageById(connectionId, request.Answer(result, () => request.ToResponse(user!)));

        if (result != null)
            _logger.LogTrace("User {SenderId} failed to change the avatar of user {Id}: {Result}", request.SenderId, userId, result);

        return HandleMessageResult.NoError;
    }

    private async Task<ErrorCode?> SetAvatar(CoordinatorSetAvatarRequest request, int userId)
    {
        var uploadId = request.UploadId?.ToLowerInvariant();

        if (uploadId != null && userId != request.SenderId)
            return ErrorCode.InsufficientPermissions;

        if (!_userManager.TryGetConnectedUser(userId, out var user) || user!.IsSystem)
            return ErrorCode.UserNotFound;

        var previousUploadId = AvatarAddress.UploadIdOf(user.AvatarUrl);

        if (uploadId != null && !await _media.AttachAvatarAsync(uploadId, request.SenderId))
            return ErrorCode.UploadNotFound;

        var result = await _userManager.SetAvatar(userId, uploadId == null ? null : AvatarAddress.Of(uploadId));

        if (result != null)
        {
            if (uploadId != null)
                await _media.RemoveAvatarAsync(uploadId);

            return result;
        }

        if (previousUploadId != null && previousUploadId != uploadId)
            await _media.RemoveAvatarAsync(previousUploadId);

        return null;
    }
}
