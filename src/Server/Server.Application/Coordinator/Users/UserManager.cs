using Shared.Enums;
using Microsoft.Extensions.Logging;
using Server.Core.Models.User;
using Server.Application.Coordinator.Connections;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Authentication;
using Server.Core.Models.Coordinator;
using Shared.Enums.User;

namespace Server.Application.Coordinator.Users;

public class UserManager : IUserManager
{
    public const int MaxConnectionsPerUser = 5;

    public event Action<UserModel>? UserConnected;
    public event Action<UserModel>? UserUpdated;
    public event Action<UserModel, string>? UserDisconnected;
    public event Action<int>? UserDeleted;
    
    // The system user is attached too, but it is not one of the users online
    public int ConnectedUsersCount => _context.AttachedUsers.Values.Count(user => !user.IsSystem);
    
    private readonly IUserManagerContext _context;
    private readonly IUserRepository _userRepository;
    private readonly ILogger _logger;
    private readonly NameProvider _nameProvider;
    private readonly SemaphoreSlim _admissionLock = new(1, 1);
    private readonly SemaphoreSlim _createUserLock = new(1, 1);
    private readonly SemaphoreSlim _renameLock = new(1, 1);
    // The roles of users not connected are read and announced one after another
    private readonly SemaphoreSlim _offlineRolesLock = new(1, 1);
    // The online limit, the user and the connection they use change under one lock, so two logins cannot take the
    // last place and a connection closing during a login cannot leave its user attached
    private readonly Lock _attachLock = new();
    // The only map of users to their connections; written under the attach lock alone
    private readonly IConnectionsContext _connections;
    private readonly string _coordinatorInstanceId;
    // Without it the users are not limited
    private readonly ICoordinatorInstanceContext? _coordinatorContext;

    public UserManager(IUserManagerContext context, 
        ILogger logger, 
        string coordinatorInstanceId,
        IUserRepository userRepository,
        ICoordinatorInstanceContext? coordinatorContext = null,
        IConnectionsContext? connections = null)
    {
        _context = context;
        _connections = connections ?? new ConnectionsContext();
        _coordinatorContext = coordinatorContext;
        _logger = logger;
        _coordinatorInstanceId = coordinatorInstanceId;
        _userRepository = userRepository;
        _nameProvider = new NameProvider();
    }

    public Task<UserAttachment> RegisterUser(UserRecoveryKeys recoveryKeys, IClientConnection clientConnection) =>
        AdmitAsync(() => RegisterUserCore(recoveryKeys, clientConnection));

    public Task<UserAttachment> AttachUser(int userId, IClientConnection clientConnection) =>
        AdmitAsync(() => AttachUserCore(userId, clientConnection));

    private async Task<UserAttachment> AdmitAsync(Func<Task<UserAttachment>> attach)
    {
        // Keep registration and attachment together so concurrent logins cannot register users for one online slot.
        await _admissionLock.WaitAsync();
        try { return await attach(); }
        finally { _admissionLock.Release(); }
    }

    private async Task<UserAttachment> RegisterUserCore(UserRecoveryKeys recoveryKeys, IClientConnection clientConnection)
    {
        // Checked before anything is created, so a full server does not register users it then turns away
        if (IsOnlineLimitReached(exceptUserId: null))
            return UserAttachment.Rejected(AttachUserResult.OnlineLimitReached);

        var user = await CreateUserAsync(recoveryKeys, clientConnection);
        var attachment = Attach(user, clientConnection);
        if (attachment.Result == AttachUserResult.Attached)
            await _userRepository.UpdateAsync(attachment.User!);
        return attachment;
    }

    private async Task<UserAttachment> AttachUserCore(int userId, IClientConnection clientConnection)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null || user.CoordinatorInstanceId != _coordinatorInstanceId || user.IsSystem)
            return UserAttachment.Rejected(AttachUserResult.UnknownUser);

        user.MarkConnected(clientConnection.IpEndpoint, DateTimeOffset.UtcNow);
        var attachment = Attach(user, clientConnection);
        if (attachment.Result == AttachUserResult.Attached) await _userRepository.UpdateAsync(attachment.User!);
        return attachment;
    }

    private UserAttachment Attach(UserModel? user, IClientConnection clientConnection)
    {
        if (user == null)
            return UserAttachment.Rejected(AttachUserResult.UserLimitReached);
        if (user.IsBanned)
            return UserAttachment.Rejected(AttachUserResult.Banned);

        bool cameOnline;
        var deviceChanged = false;
        lock (_attachLock)
        {
            // Closed while the user was looked up: its close had no user to detach, so none is attached
            if (clientConnection.IsClosed)
                return UserAttachment.Rejected(AttachUserResult.ConnectionClosed);

            // A user connecting again, or from another device, takes their own place
            // Bots take no place among the online users
            if (!user.IsSystem && !user.IsBot && IsOnlineLimitReached(exceptUserId: user.Id))
                return UserAttachment.Rejected(AttachUserResult.OnlineLimitReached);

            // The system user has no connection of its own
            if (!user.IsSystem)
            {
                var connections = _connections.UserConnections.GetValueOrDefault(user.Id) ?? [];
                if (!connections.Contains(clientConnection.ConnectionId))
                {
                    if (connections.Length >= MaxConnectionsPerUser)
                        return UserAttachment.Rejected(AttachUserResult.ConnectionLimitReached);

                    _connections.UserConnections[user.Id] = [..connections, clientConnection.ConnectionId];
                }
                clientConnection.UserId = user.Id;
            }

            // Connected from another device already: the user stays the one attached, with what changed meanwhile
            cameOnline = !_context.AttachedUsers.TryGetValue(user.Id, out var attached);
            if (cameOnline)
            {
                user.SetConnected(true);
                _context.AttachedUsers[user.Id] = user;
            }
            else
            {
                attached!.MarkConnected(clientConnection.IpEndpoint, DateTimeOffset.UtcNow);
                user = attached;
            }

            // The device connected last is the one others see
            if (!user.IsSystem)
                deviceChanged = user.AddDevice(clientConnection.ConnectionId, clientConnection.Device);
        }

        // Raised once the connection is the user's, so what is sent to the user reaches it
        if (cameOnline)
            UserConnected?.Invoke(user);
        else if (deviceChanged)
            UserUpdated?.Invoke(user);
        if (!user.IsSystem)
            _logger.LogTrace("User with id {Id} connected on {ConnectionId}.", user.Id, clientConnection.ConnectionId);

        return new UserAttachment(AttachUserResult.Attached, user);
    }

    public async Task<ErrorCode?> ChangeUsername(int userId, string newUsername)
    {
        // A user who is not connected is renamed in the database
        var user = await GetUserAsync(userId);
        if (user == null || user.IsSystem || user.CoordinatorInstanceId != _coordinatorInstanceId)
            return ErrorCode.UserNotFound;

        newUsername = newUsername.Trim();
        if (user.Username == newUsername)
            return null;

        // Check and rename under one lock, so two users cannot take the same free name at once
        await _renameLock.WaitAsync();
        try
        {
            // Names are told apart ignoring case: a login by username finds the user by it
            var takenByConnected = _context.AttachedUsers.Values
                .Any(other => other.Id != userId
                              && string.Equals(other.Username, newUsername, StringComparison.OrdinalIgnoreCase));
            if (takenByConnected || await _userRepository.IsUsernameTakenAsync(newUsername, userId))
                return ErrorCode.NameTaken;

            user.Rename(newUsername);
            await _userRepository.UpdateAsync(user);

            // Connected meanwhile, with the name read before the rename
            if (_context.AttachedUsers.TryGetValue(userId, out var attached) && attached != user)
            {
                attached.Rename(newUsername);
                user = attached;
            }
        }
        finally
        {
            _renameLock.Release();
        }

        _logger.LogInformation("User {Id} renamed to {Name}.", userId, newUsername);
        UserUpdated?.Invoke(user);
        return null;
    }
        
    public async Task<ErrorCode?> SetAvatar(int userId, string? avatarUrl)
    {
        if (!_context.AttachedUsers.TryGetValue(userId, out var user) || user.IsSystem)
            return ErrorCode.UserNotFound;

        if (user.AvatarUrl == avatarUrl)
            return null;

        user.SetAvatar(avatarUrl);
        await _userRepository.UpdateAsync(user);

        _logger.LogInformation("User {Id} changed the avatar.", userId);
        UserUpdated?.Invoke(user);
        return null;
    }

    public async Task<ErrorCode?> SetStatus(int userId, UserStatus status)
    {
        if (!UserModel.CanPick(status))
            return ErrorCode.InvalidValue;

        if (!_context.AttachedUsers.TryGetValue(userId, out var user) || user.IsSystem)
            return ErrorCode.UserNotFound;

        if (user.Status == status)
            return null;

        user.SetStatus(status);
        await _userRepository.UpdateAsync(user);

        _logger.LogTrace("User {Id} is now {Status}.", userId, status);
        UserUpdated?.Invoke(user);
        return null;
    }

    public ErrorCode? SetIdleLevel(int userId, string connectionId, IdleLevel level)
    {
        if (!Enum.IsDefined(level))
            return ErrorCode.InvalidValue;

        if (!_context.AttachedUsers.TryGetValue(userId, out var user) || user.IsSystem)
            return ErrorCode.UserNotFound;

        // A bot is never idle
        if (user.IsBot)
            return null;

        if (!user.SetIdleLevel(connectionId, level))
            return null;

        _logger.LogTrace("User {Id} is now seen {Presence}.", userId, user.Presence);
        UserUpdated?.Invoke(user);
        return null;
    }

    public async Task<ErrorCode?> SetState(int userId, UserState state)
    {
        if (state is not (UserState.Normal or UserState.Suspended or UserState.Banned))
            return ErrorCode.InvalidValue;

        var user = await GetUserAsync(userId);
        if (user == null || user.IsSystem || user.CoordinatorInstanceId != _coordinatorInstanceId)
            return ErrorCode.UserNotFound;

        if (user.State == state)
            return null;

        ApplyState(user, state);
        await _userRepository.UpdateAsync(user);

        // Connected meanwhile, with the state read before the change
        if (_context.AttachedUsers.TryGetValue(userId, out var attached) && attached != user)
        {
            ApplyState(attached, state);
            user = attached;
        }

        _logger.LogInformation("User {Id} is now {State}.", userId, state);
        UserUpdated?.Invoke(user);
        return null;
    }

    public async Task<ErrorCode?> DeleteUser(int userId)
    {
        var user = await GetUserAsync(userId);
        if (user == null || user.IsSystem || user.CoordinatorInstanceId != _coordinatorInstanceId)
            return ErrorCode.UserNotFound;

        await _userRepository.RemoveAsync(userId);
        DetachUser(userId, "User is deleted");
        await ClearBotOwnerAsync(userId);

        _logger.LogInformation("User {Id} ({Name}) deleted.", userId, user.Username);
        UserDeleted?.Invoke(userId);
        return null;
    }

    public async Task<(ErrorCode? Error, UserModel? Bot)> CreateBot(int ownerUserId, string username, bool isDebug)
    {
        username = username.Trim();
        if (username.Length == 0)
            return (ErrorCode.InvalidValue, null);

        // The limit under the lock of new users, the name under the lock of renames, as for people
        await _createUserLock.WaitAsync();
        try
        {
            var maxBots = _coordinatorContext?.Model.MaxBots ?? CoordinatorInstanceModel.DefaultMaxBots;
            if (!isDebug && await _userRepository.CountBotsAsync(_coordinatorInstanceId) >= maxBots)
                return (ErrorCode.BotLimitReached, null);

            await _renameLock.WaitAsync();
            try
            {
                var takenByConnected = _context.AttachedUsers.Values
                    .Any(other => string.Equals(other.Username, username, StringComparison.OrdinalIgnoreCase));
                if (takenByConnected || await _userRepository.IsUsernameTakenAsync(username, 0))
                    return (ErrorCode.NameTaken, null);

                var bot = UserModel.CreateBot(_coordinatorInstanceId, username, ownerUserId, isDebug,
                    DateTimeOffset.UtcNow);
                bot.Id = await _userRepository.AddAsync(bot, recoveryKeys: null);
                _logger.LogInformation("User {OwnerId} made the {Kind} {Id} ({Name}).", ownerUserId, bot.Kind, bot.Id,
                    bot.Username);
                return (null, bot);
            }
            finally
            {
                _renameLock.Release();
            }
        }
        finally
        {
            _createUserLock.Release();
        }
    }

    public async Task<IReadOnlyList<UserModel>> GetBotsAsync()
    {
        var bots = await _userRepository.GetBotsAsync(_coordinatorInstanceId);
        // The connected ones as they are attached, with their status and device
        return bots.Select(bot => _context.AttachedUsers.GetValueOrDefault(bot.Id) ?? bot).ToList();
    }

    // The bots of a deleted user stay, without an owner
    private async Task ClearBotOwnerAsync(int ownerUserId)
    {
        foreach (var botId in await _userRepository.ClearBotOwnerAsync(ownerUserId))
        {
            if (_context.AttachedUsers.TryGetValue(botId, out var bot))
            {
                bot.ClearOwner();
                UserUpdated?.Invoke(bot);
            }
        }
    }

    private static void ApplyState(UserModel user, UserState state)
    {
        switch (state)
        {
            case UserState.Banned:
                user.Ban();
                break;
            case UserState.Suspended:
                user.Suspend();
                break;
            default:
                user.Reinstate();
                break;
        }
    }

    public async Task SetRolesAsync(int userId, IEnumerable<int> roleIds)
    {
        // Connected: taken at once, in the order the changes come
        if (_context.AttachedUsers.TryGetValue(userId, out var attached))
        {
            if (attached.IsSystem)
                return;

            attached.SetRoles(roleIds);
            UserUpdated?.Invoke(attached);
            return;
        }

        // Not connected: the roles are saved already. Read one after another, so the last one announced was read
        // after every change before it
        await _offlineRolesLock.WaitAsync();
        try
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null || user.IsSystem || user.CoordinatorInstanceId != _coordinatorInstanceId)
                return;

            // Connected meanwhile, with the roles as saved
            if (_context.AttachedUsers.TryGetValue(userId, out attached))
                user = attached;

            UserUpdated?.Invoke(user);
        }
        finally
        {
            _offlineRolesLock.Release();
        }
    }

    public void DetachUser(int userId, string reason)
    {
        UserModel? user;
        lock (_attachLock)
        {
            if (!_context.AttachedUsers.TryRemove(userId, out user))
                return;
            _connections.UserConnections.TryRemove(userId, out _);
        }

        OnDetached(user, reason);
    }

    public bool DetachConnection(IClientConnection connection, string reason)
    {
        UserModel? user = null;
        UserModel? stillConnected = null;
        lock (_attachLock)
        {
            if (connection.UserId is not { } userId
                || !_connections.UserConnections.TryGetValue(userId, out var connections)
                || !connections.Contains(connection.ConnectionId))
                return false;

            // Still connected from another device, which others may see now
            if (connections.Length > 1)
            {
                _connections.UserConnections[userId] = connections.Where(id => id != connection.ConnectionId).ToArray();
                if (_context.AttachedUsers.TryGetValue(userId, out var attached)
                    && attached.RemoveDevice(connection.ConnectionId))
                    stillConnected = attached;
            }
            else
            {
                _connections.UserConnections.TryRemove(userId, out _);
                if (!_context.AttachedUsers.TryRemove(userId, out user))
                    return false;
            }
        }

        if (stillConnected != null)
        {
            UserUpdated?.Invoke(stillConnected);
            return false;
        }
        if (user == null)
            return false;

        OnDetached(user, reason);
        return true;
    }

    private void OnDetached(UserModel user, string reason)
    {
        user.SetConnected(false);
        UserDisconnected?.Invoke(user, reason);
        if (!user.IsSystem)
            _logger.LogInformation("User {Name} disconnected. Reason: {Reason}", user.Username, reason);
    }

    public bool TryGetConnectedUser(int userId, out UserModel? user)
    {
        if (_context.AttachedUsers.TryGetValue(userId, out var u))
        {
            user = u;
            return true;
        }
        user = null;
        return false;
    }

    public Task<UserModel?> GetUserAsync(int userId) =>
        _context.AttachedUsers.TryGetValue(userId, out var user)
            ? Task.FromResult<UserModel?>(user)
            : _userRepository.GetByIdAsync(userId);
    
    public IReadOnlyDictionary<int, UserModel> GetAllConnectedUsers()
    {
        return _context.AttachedUsers.AsReadOnly();
    }
    
    public async Task<IReadOnlyDictionary<int, UserModel>> GetAllUsersAsync()
    {
        var users = await _userRepository.GetAllAsync();

        // The saved copy of a connected user is not connected, and may be older
        foreach (var (id, attached) in _context.AttachedUsers)
        {
            if (users.ContainsKey(id))
                users[id] = attached;
        }

        return users.AsReadOnly();
    }

    public void ForceDetachAllUsers()
    {
        lock (_attachLock)
        {
            foreach (var user in _context.AttachedUsers.Values)
                user.SetConnected(false);
            _context.AttachedUsers.Clear();
            _connections.UserConnections.Clear();
        }
    }

    /// <returns>Null when the coordinator cannot register another user.</returns>
    private async Task<UserModel?> CreateUserAsync(UserRecoveryKeys recoveryKeys, IClientConnection clientConnection)
    {
        const string namePattern = "DefaultUsername";

        // Pick the name and save under one lock, so two new users cannot get the same generated name
        // or both take the last free place
        await _createUserLock.WaitAsync();
        try
        {
            var maxUsers = _coordinatorContext?.Model.MaxUsers ?? CoordinatorInstanceModel.NoUserLimit;
            if (maxUsers != CoordinatorInstanceModel.NoUserLimit
                && await _userRepository.CountAsync(_coordinatorInstanceId) >= maxUsers)
            {
                _logger.LogInformation("New user rejected: the coordinator has {MaxUsers} users already.", maxUsers);
                return null;
            }

            var takenNames = (await _userRepository.GetUsernamesStartingWithAsync(namePattern))
                .Concat(GetAllConnectedUsers().Values.Select(user => user.Username))
                .ToHashSet();

            var newUser = UserModel.Create(_coordinatorInstanceId,
                _nameProvider.GenerateBaseName(namePattern, takenNames), clientConnection.IpEndpoint, DateTimeOffset.UtcNow);

            newUser.Id = await _userRepository.AddAsync(newUser, recoveryKeys);
            return newUser;
        }
        finally
        {
            _createUserLock.Release();
        }
    }

    private bool IsOnlineLimitReached(int? exceptUserId)
    {
        if (exceptUserId is { } userId && _context.AttachedUsers.ContainsKey(userId))
            return false;

        var maxOnlineUsers = _coordinatorContext?.Model.MaxOnlineUsers ?? CoordinatorInstanceModel.NoUserLimit;
        if (maxOnlineUsers == CoordinatorInstanceModel.NoUserLimit)
            return false;

        var online = _context.AttachedUsers.Values.Count(user => !user.IsSystem && !user.IsBot && user.Id != exceptUserId);
        return online >= maxOnlineUsers;
    }
}
