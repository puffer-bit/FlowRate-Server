using Microsoft.Extensions.Logging;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Users;
using Server.Application.Mappers;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Notifications.CoordinatorInfo;
using Shared.Models.Requests.Coordinator;

namespace Server.Application.Coordinator.Handlers.Users;

/// <summary>
/// Bans, freezes or reinstates a user. A banned user is disconnected; a frozen one leaves the voice sessions and
/// stays connected to read.
/// </summary>
public class SetUserStateHandler : IMessageHandler<CoordinatorSetUserStateRequest>
{
    private readonly ILogger _logger;
    private readonly IMessageSender _messageSender;
    private readonly IUserManager _userManager;
    private readonly IConnectionManager _connectionManager;
    private readonly IHybridSessionManager _hybridSessionManager;

    public SetUserStateHandler(ILogger logger, IMessageSender messageSender, IUserManager userManager,
        IConnectionManager connectionManager, IHybridSessionManager hybridSessionManager)
    {
        _logger = logger;
        _messageSender = messageSender;
        _userManager = userManager;
        _connectionManager = connectionManager;
        _hybridSessionManager = hybridSessionManager;
    }

    public async Task<HandleMessageResult> HandleMessage(CoordinatorSetUserStateRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var userId = request.TargetUserId;
        if (userId == request.SenderId || userId == UserModel.SystemUserId)
        {
            _messageSender.SendMessageById(connectionId, request.ToError(ErrorCode.CannotTargetSelf));
            return HandleMessageResult.NoError;
        }

        var result = await _userManager.SetState(userId, request.State);
        var user = result == null
            ? (await _userManager.GetUserAsync(userId))?.ToDto(request.SenderId)
            : null;
        _messageSender.SendMessageById(connectionId, request.Answer(result, () => request.ToResponse(user!)));

        if (result != null)
        {
            _logger.LogTrace("User {SenderId} failed to make user {Id} {State}: {Result}", request.SenderId, userId,
                request.State, result);
            return HandleMessageResult.NoError;
        }

        if (request.State == UserState.Banned)
            Disconnect(userId);
        else if (request.State == UserState.Suspended)
            await _hybridSessionManager.RemoveUserFromAllSessionsAsync(userId);

        return HandleMessageResult.NoError;
    }

    // Told first, so the client does not take the closed connection for a network failure
    private void Disconnect(int userId)
    {
        foreach (var connection in _connectionManager.GetUserConnections(userId))
        {
            connection.Send(new CoordinatorUserBannedNotification(userId));
            connection.Close();
        }
    }
}
