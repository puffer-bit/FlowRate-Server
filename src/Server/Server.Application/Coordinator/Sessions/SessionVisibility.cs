using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Roles;
using Server.Application.Coordinator.Users;
using Server.Application.Mappers;
using Server.Core.Models;
using Server.Core.Models.HybridSession;
using Server.Core.Models.TextChat;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Notifications.SessionInfo;

namespace Server.Application.Coordinator.Sessions;

/// <summary>
/// Which users see which sessions. A user who may not see a session gets nothing of it: not the session, not
/// what happens in it. Keeps the session list of every connected user in step when what it may see changes,
/// telling it about a session as created or deleted.
/// </summary>
public sealed class SessionVisibility : IDisposable
{
    private readonly IMessageSender _messageSender;
    private readonly Lock _lock = new();
    // The sessions each user was last told about, from its first full list on
    private readonly Dictionary<int, HashSet<int>> _shown = new();

    private ICoordinatorPermissions? _permissions;
    private ISessionManager? _sessions;
    private IUserManager? _users;

    public SessionVisibility(IMessageSender messageSender)
    {
        _messageSender = messageSender;
    }

    /// <summary>Starts deciding by the roles; until then nothing is shown to anyone.</summary>
    public void Watch(ICoordinatorPermissions permissions, ISessionManager sessions, IUserManager users)
    {
        _permissions = permissions;
        _sessions = sessions;
        _users = users;
        permissions.PermissionsChanged += Refresh;
    }

    public bool CanView(int userId, SessionModel session) =>
        _permissions?.HasIn(userId, session, Permission.ViewSessions) ?? false;

    /// <summary>The sessions the user may see, in their order. They become what the user was told about.</summary>
    public IReadOnlyList<SessionModel> ShowAll(int userId)
    {
        var visible = (_sessions?.GetAllSessionModels() ?? [])
            .Where(session => CanView(userId, session))
            .ToList();
        lock (_lock)
            _shown[userId] = visible.Select(session => session.Id).ToHashSet();
        return visible;
    }

    /// <summary>The session, when the user may see it. It becomes one the user was told about.</summary>
    public SessionModel? Show(int userId, int sessionId)
    {
        if (_sessions == null || !_sessions.TryGetSessionModel(sessionId, out var session)
                              || !CanView(userId, session))
            return null;

        lock (_lock)
            if (_shown.TryGetValue(userId, out var shown))
                shown.Add(sessionId);
        return session;
    }

    /// <summary>Sends something that happened in a session to the connected users who may see it.</summary>
    public void Send(int sessionId, BaseMessage message)
    {
        if (_sessions != null && _sessions.TryGetSessionModel(sessionId, out var session))
            _messageSender.BroadcastMessage(ConnectedUsers().Where(userId => CanView(userId, session)).ToList(),
                message);
    }

    public void Created(SessionModel session)
    {
        var viewers = ConnectedUsers().Where(userId => CanView(userId, session)).ToList();
        lock (_lock)
            foreach (var userId in viewers)
                if (_shown.TryGetValue(userId, out var shown))
                    shown.Add(session.Id);
        _messageSender.BroadcastMessage(viewers, CreatedNotification(session));
    }

    /// <summary>
    /// Sends the changed session to those who may see it; those who no longer may get it deleted, those who now
    /// may get it created.
    /// </summary>
    public void Reconfigured(SessionModel session)
    {
        foreach (var userId in ConnectedUsers())
            Update(userId, session, ReconfiguredNotification(session));
    }

    /// <summary>Tells everyone; a client that never had the session ignores it.</summary>
    public void Deleted(int sessionId, BaseMessage notification)
    {
        lock (_lock)
            foreach (var shown in _shown.Values)
                shown.Remove(sessionId);
        _messageSender.BroadcastMessage(notification);
    }

    /// <summary>Brings the session lists of the users in step with what they may see now.</summary>
    public void Refresh(IReadOnlyCollection<int> userIds)
    {
        var sessions = _sessions?.GetAllSessionModels() ?? [];
        foreach (var userId in userIds)
            foreach (var session in sessions)
                Update(userId, session, null);
    }

    private void Update(int userId, SessionModel session, BaseMessage? unchanged)
    {
        var canView = CanView(userId, session);
        bool? wasShown;
        lock (_lock)
        {
            if (_shown.TryGetValue(userId, out var shown))
            {
                wasShown = shown.Contains(session.Id);
                if (canView)
                    shown.Add(session.Id);
                else
                    shown.Remove(session.Id);
            }
            else
                // Its list is still to come, and will be right
                wasShown = null;
        }

        var message = (wasShown, canView) switch
        {
            (true, false) => DeletedNotification(session),
            (false, true) => CreatedNotification(session),
            (_, true) => unchanged,
            _ => null
        };
        if (message != null)
            _messageSender.SendMessageByUserId(userId, message);
    }

    private IEnumerable<int> ConnectedUsers() =>
        _users?.GetAllConnectedUsers().Keys.ToList() ?? [];

    private static BaseMessage CreatedNotification(SessionModel session) => session switch
    {
        ChatSessionModel chat => new ChatSessionCreatedNotification(chat.ToDto()),
        HybridSessionModel hybrid => new HybridSessionCreatedNotification(hybrid.ToDto()),
        _ => throw new ArgumentOutOfRangeException(nameof(session), session.SessionType, null)
    };

    private static BaseMessage ReconfiguredNotification(SessionModel session) => session switch
    {
        ChatSessionModel chat => new ChatSessionReconfiguredNotification(chat.ToDto()),
        HybridSessionModel hybrid => new HybridSessionReconfiguredNotification(hybrid.ToDto()),
        _ => throw new ArgumentOutOfRangeException(nameof(session), session.SessionType, null)
    };

    private static BaseMessage DeletedNotification(SessionModel session) => session switch
    {
        ChatSessionModel => new ChatSessionDeletedNotification(session.Id),
        HybridSessionModel => new HybridSessionDeletedNotification(session.Id),
        _ => throw new ArgumentOutOfRangeException(nameof(session), session.SessionType, null)
    };

    public void Dispose()
    {
        if (_permissions != null)
            _permissions.PermissionsChanged -= Refresh;
    }
}
