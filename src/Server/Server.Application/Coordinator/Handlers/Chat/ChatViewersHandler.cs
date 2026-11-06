using System.Text.Json;
using Microsoft.Extensions.Logging;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Roles;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Users;
using Server.Application.Mappers;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Models.DataTransferObjects;
using Shared.Models.Notifications.SessionInfo;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Permissions;

namespace Server.Application.Coordinator.Handlers.Chat;

/// <summary>One audience subscription per device. Snapshots and deltas share a serial write order.</summary>
public sealed class ChatViewersHandler : IMessageHandler<ChatViewersRequest>, IDisposable
{
    private sealed record Subscription(int UserId, int ChatId, Guid Id,
        Dictionary<int, string> Rows);

    private readonly Dictionary<string, Subscription> _subscriptions = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IChatSessionManager _chats;
    private readonly IUserManager _users;
    private readonly ICoordinatorPermissions _permissions;
    private readonly IMessageSender _sender;
    private readonly IConnectionManager _connections;
    private readonly SessionVisibility _visibility;
    private readonly ILogger _logger;
    private bool _disposed;

    public ChatViewersHandler(IChatSessionManager chats, IUserManager users, ICoordinatorPermissions permissions,
        IMessageSender sender, IConnectionManager connections, SessionVisibility visibility, ILogger logger)
    {
        (_chats, _users, _permissions, _sender, _connections, _visibility, _logger) =
            (chats, users, permissions, sender, connections, visibility, logger);
        users.UserConnected += OnUser;
        users.UserUpdated += OnUser;
        users.UserDisconnected += OnDisconnected;
        users.UserDeleted += OnDeleted;
        permissions.PermissionsChanged += OnPermissions;
        visibility.AudienceChanged += OnChanged;
        connections.ConnectionClosed += OnConnectionClosed;
    }

    public async Task<HandleMessageResult> HandleMessage(ChatViewersRequest? request, string connectionId)
    {
        if (request == null) return HandleMessageResult.ForbiddenRequest;
        await _gate.WaitAsync();
        try
        {
            if (_disposed) return HandleMessageResult.ForbiddenRequest;
            if (!request.Subscribe)
            {
                if (_subscriptions.TryGetValue(connectionId, out var old) && old.Id == request.SubscriptionId)
                    _subscriptions.Remove(connectionId);
                _sender.SendMessageById(connectionId, request.ToResponse([]));
                return HandleMessageResult.NoError;
            }

            // Check again here: access may have changed after request authorization.
            if (!_chats.TryGetSession(request.ChatId, out var chat)
                || !_permissions.HasIn(request.SenderId, chat, Permission.ViewSessions))
            {
                _sender.SendMessageById(connectionId, request.ToError(ErrorCode.ChatNotFound));
                return HandleMessageResult.ForbiddenRequest;
            }

            var users = await GetRows(request.ChatId, request.SenderId);
            _subscriptions[connectionId] = new(request.SenderId, request.ChatId, request.SubscriptionId,
                users.ToDictionary(u => u.Id, Fingerprint));
            _sender.SendMessageById(connectionId, request.ToResponse(users));
            return HandleMessageResult.NoError;
        }
        finally { _gate.Release(); }
    }

    private async Task<List<UserDTO>> GetRows(int chatId, int viewerId)
    {
        if (!_chats.TryGetSession(chatId, out var chat)
            || !_permissions.HasIn(viewerId, chat, Permission.ViewSessions)) return [];
        var roles = _permissions.Roles;
        return (await _users.GetAllUsersAsync()).Values
            .Where(user => !user.IsSystem
                && SessionPermissions.Resolve(roles.PermissionsOf(user.RoleIds), chat.PermissionOverrides,
                    roles.Everyone.Id, user.RoleIds).HasFlag(Permission.ViewSessions))
            .OrderBy(user => user.Username, StringComparer.OrdinalIgnoreCase)
            .Select(user => user.ToDto(viewerId)).ToList();
    }

    private static string Fingerprint(UserDTO user) => JsonSerializer.Serialize(user);
    private void OnUser(UserModel user) => _ = RefreshAsync(removedUser: user.IsBanned ? user.Id : null);
    private void OnDisconnected(UserModel _, string reason) => OnChanged();
    private void OnDeleted(int userId) => _ = RefreshAsync(removedUser: userId);
    private void OnPermissions(IReadOnlyCollection<int> _) => OnChanged();
    private void OnChanged() => _ = RefreshAsync();
    private void OnConnectionClosed(string connectionId) => _ = RefreshAsync(connectionId);

    private async Task RefreshAsync(string? closedConnection = null, int? removedUser = null)
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed) return;
            if (closedConnection != null) _subscriptions.Remove(closedConnection);
            foreach (var (connectionId, subscription) in _subscriptions.ToArray())
            {
                var rows = subscription.UserId == removedUser
                    ? new List<UserDTO>() : await GetRows(subscription.ChatId, subscription.UserId);
                var current = rows.ToDictionary(u => u.Id, Fingerprint);
                var removed = subscription.Rows.Keys.Except(current.Keys).ToList();
                var changed = rows.Where(u => !subscription.Rows.TryGetValue(u.Id, out var previous)
                    || previous != current[u.Id]).ToList();
                if (changed.Count > 0 || removed.Count > 0)
                    _sender.SendMessageById(connectionId, new ChatViewersChangedNotification(
                        subscription.ChatId, subscription.Id, changed, removed));
                _subscriptions[connectionId] = subscription with { Rows = current };
                if (subscription.UserId == removedUser || !_chats.TryGetSession(subscription.ChatId, out var chat)
                    || !_permissions.HasIn(subscription.UserId, chat, Permission.ViewSessions))
                    _subscriptions.Remove(connectionId);
            }
        }
        catch (Exception exception) { _logger.LogError(exception, "Updating chat audiences failed."); }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        _disposed = true;
        _users.UserConnected -= OnUser;
        _users.UserUpdated -= OnUser;
        _users.UserDisconnected -= OnDisconnected;
        _users.UserDeleted -= OnDeleted;
        _permissions.PermissionsChanged -= OnPermissions;
        _visibility.AudienceChanged -= OnChanged;
        _connections.ConnectionClosed -= OnConnectionClosed;
    }
}
