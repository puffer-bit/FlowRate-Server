using Microsoft.Extensions.Logging;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Users;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Notifications.CoordinatorInfo;
using Shared.Models.Requests.Coordinator;

namespace Server.Application.Coordinator.Handlers.Users;

/// <summary>Deletes a user and closes their connection.</summary>
public class DeleteUserHandler : IMessageHandler<CoordinatorDeleteUserRequest>
{
    private readonly ILogger _logger;
    private readonly IMessageSender _messageSender;
    private readonly IUserManager _userManager;
    private readonly IConnectionManager _connectionManager;

    public DeleteUserHandler(ILogger logger, IMessageSender messageSender, IUserManager userManager,
        IConnectionManager connectionManager)
    {
        _logger = logger;
        _messageSender = messageSender;
        _userManager = userManager;
        _connectionManager = connectionManager;
    }

    public async Task<HandleMessageResult> HandleMessage(CoordinatorDeleteUserRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var userId = request.UserTargetId;
        if (userId == request.SenderId || userId == UserModel.SystemUserId)
        {
            _messageSender.SendMessageById(connectionId, request.ToResponse(ModerateUserResult.CannotTargetSelf));
            return HandleMessageResult.NoError;
        }

        var connections = _connectionManager.GetUserConnections(userId);

        var result = await _userManager.DeleteUser(userId);
        _messageSender.SendMessageById(connectionId, request.ToResponse(result));

        if (result != ModerateUserResult.NoError)
        {
            _logger.LogTrace("User {SenderId} failed to delete user {Id}: {Result}", request.SenderId, userId, result);
            return HandleMessageResult.NoError;
        }

        foreach (var connection in connections)
        {
            connection.Send(new CoordinatorUserDeletedNotification(userId));
            connection.Close();
        }

        return HandleMessageResult.NoError;
    }

    public BaseMessage? CreateFailureResponse(CoordinatorDeleteUserRequest request) =>
        request.ToResponse(ModerateUserResult.InternalError);
}
