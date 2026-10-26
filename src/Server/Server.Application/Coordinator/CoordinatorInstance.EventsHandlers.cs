using Microsoft.Extensions.Logging;
using Server.Application.Mappers;
using Server.Core.Models.User;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.DataTransferObjects;
using Shared.Models.Notifications.CoordinatorInfo;

namespace Server.Application.Coordinator;

/// <summary>Reactions to connections and users coming and going.</summary>
public partial class CoordinatorInstance
{
    private void SubscribeToEvents()
    {
        _connectionManager.ConnectionClosed += OnConnectionClosed;

        _heartbeatManager.TimeoutOccured += OnHeartbeatTimeout;

        _userManager.UserConnected += OnUserConnected;
        _userManager.UserUpdated += OnUserUpdated;
        _userManager.UserDisconnected += OnUserDisconnected;
        _userManager.UserDeleted += OnUserDeleted;
    }

    private void UnsubscribeFromEvents()
    {
        _connectionManager.ConnectionClosed -= OnConnectionClosed;

        _heartbeatManager.TimeoutOccured -= OnHeartbeatTimeout;

        _userManager.UserConnected -= OnUserConnected;
        _userManager.UserUpdated -= OnUserUpdated;
        _userManager.UserDisconnected -= OnUserDisconnected;
        _userManager.UserDeleted -= OnUserDeleted;
    }

    private void OnHeartbeatTimeout(string connectionId) => DetachConnectionUser(connectionId, "Connection is timed out");

    private void OnConnectionClosed(string connectionId) => DetachConnectionUser(connectionId, "Connection is closed");

    private void DetachConnectionUser(string connectionId, string reason)
    {
        if (!_connectionManager.TryRemoveConnectionById(connectionId, out var connection)
            || connection.UserId is not { } userId)
            return;

        // The last connection of the user takes them out of every session; another one only of what was on it
        if (!_userManager.DetachConnection(connection, reason))
            _ = RemoveConnectionFromAllSessionsAsync(userId, connectionId);
    }

    private void OnUserConnected(UserModel user)
    {
        BroadcastUser(user, dto => new CoordinatorUserConnectedNotification(dto));
        if (!user.IsSystem)
            BroadcastOnlineUsersCount();
    }

    private void OnUserUpdated(UserModel user)
    {
        BroadcastUser(user, dto => new CoordinatorUserUpdatedNotification(dto));
    }

    private void OnUserDisconnected(UserModel user, string? reason = null)
    {
        _ = RemoveUserFromAllSessionsAsync(user.Id);

        _messageSender.BroadcastMessage(new CoordinatorUserDisconnectedNotification(user.Id, reason));
        // Clients learn of statuses from updates only; an invisible user looked offline already
        if (user.Status != UserStatus.Invisible)
            _messageSender.BroadcastMessage(new CoordinatorUserUpdatedNotification(user.ToDto()));
        BroadcastOnlineUsersCount();
    }

    /// <summary>
    /// Sends the user to everyone, and to the user themselves with the status they picked when others see
    /// another one.
    /// </summary>
    private void BroadcastUser(UserModel user, Func<UserDTO, BaseMessage> notification)
    {
        if (user.Status == user.Presence)
        {
            _messageSender.BroadcastMessage(notification(user.ToDto()));
            return;
        }

        var others = _userManager.GetAllConnectedUsers().Keys.Where(id => id != user.Id);
        _messageSender.BroadcastMessage(others, notification(user.ToDto()));
        _messageSender.SendMessageByUserId(user.Id, notification(user.ToDto(user.Id)));
    }

    private void BroadcastOnlineUsersCount() =>
        _messageSender.BroadcastMessage(new CoordinatorOnlineUsersChangedNotification(_userManager.ConnectedUsersCount));

    private void OnUserDeleted(int userId)
    {
        _messageSender.BroadcastMessage(new CoordinatorUserDeletedNotification(userId));
    }

    private async Task RemoveUserFromAllSessionsAsync(int userId)
    {
        try
        {
            await _sessionManager.RemoveUserFromAllSessionsAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Removing user {UserId} from sessions failed.", userId);
        }
    }

    private async Task RemoveConnectionFromAllSessionsAsync(int userId, string connectionId)
    {
        try
        {
            await _sessionManager.RemoveConnectionFromAllSessionsAsync(userId, connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Removing user {UserId} of connection {ConnectionId} from sessions failed.", userId,
                connectionId);
        }
    }
}
