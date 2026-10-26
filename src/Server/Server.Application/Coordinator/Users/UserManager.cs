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
    private readonly SemaphoreSlim _createUserLock = new(1, 1);
    private readonly SemaphoreSlim _renameLock = new(1, 1);
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

    public async Task<UserAttachment> AttachUser(string? userIdentity, IClientConnection clientConnection)
    {
        // Checked before anything is created, so a full server does not register users it then turns away
        if (userIdentity != "system" && IsOnlineLimitReached(exceptUserId: null))
            return UserAttachment.Rejected(AttachUserResult.OnlineLimitReached);

        UserModel? user;
        // If client provided null identity -> create new user with generated identity
        if (userIdentity == null)
        {
            userIdentity = "#" + Guid.NewGuid().ToString();
            user = await CreateUserAsync(userIdentity, clientConnection);
        }
        else if (userIdentity == "system")
        {
            user = UserModel.CreateSystem(_coordinatorInstanceId, clientConnection.IpEndpoint, DateTime.UtcNow);
        }
        else
        {
            // If client provided something that might be an existing user id (GUID-like), try to resolve by id first
            if (IsProbableId(userIdentity) && await _userRepository.GetByIdentityAsync(_coordinatorInstanceId, userIdentity) is { } userById)
            {
                user = userById;
                user.MarkConnected(clientConnection.IpEndpoint, DateTime.UtcNow);
                // we have an id, but need the stored identity value from DB
                var storedIdentity = await _userRepository.GetIdentityByIdAsync(user.Id);
                if (!string.IsNullOrEmpty(storedIdentity))
                    userIdentity = storedIdentity;
            }
            else if (await _userRepository.GetByIdentityAsync(_coordinatorInstanceId, userIdentity) is { } foundedUser)
            {
                user = foundedUser;
                user.MarkConnected(clientConnection.IpEndpoint, DateTime.UtcNow);
            }
            else
            {
                _logger.LogInformation("Unknown user identity received, login rejected");
                return UserAttachment.Rejected(AttachUserResult.UnknownIdentity);
            }
        }

        if (user == null)
            return UserAttachment.Rejected(AttachUserResult.UserLimitReached);
        if (user.IsBanned)
            return UserAttachment.Rejected(AttachUserResult.Banned);

        bool cameOnline;
        lock (_attachLock)
        {
            // Closed while the user was looked up: its close had no user to detach, so none is attached
            if (clientConnection.IsClosed)
                return UserAttachment.Rejected(AttachUserResult.ConnectionClosed);

            // A user connecting again, or from another device, takes their own place
            if (!user.IsSystem && IsOnlineLimitReached(exceptUserId: user.Id))
                return UserAttachment.Rejected(AttachUserResult.OnlineLimitReached);

            // The system user has no connection of its own
            if (!user.IsSystem)
            {
                var connections = _connections.UserConnections.GetValueOrDefault(user.Id) ?? [];
                if (!connections.Contains(clientConnection.ConnectionId))
                    _connections.UserConnections[user.Id] = [..connections, clientConnection.ConnectionId];
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
                attached!.MarkConnected(clientConnection.IpEndpoint, DateTime.UtcNow);
                user = attached;
            }
        }

        // Raised once the connection is the user's, so what is sent to the user reaches it
        if (cameOnline)
            UserConnected?.Invoke(user);
        if (!user.IsSystem)
            _logger.LogTrace("User with id {Id} connected on {ConnectionId}.", user.Id, clientConnection.ConnectionId);

        return new UserAttachment(AttachUserResult.Attached, user, userIdentity);
    }

    public async Task<ChangeUsernameResult> ChangeUsername(int userId, string newUsername)
    {
        // A user who is not connected is renamed in the database
        var user = await GetUserAsync(userId);
        if (user == null || user.IsSystem || user.CoordinatorInstanceId != _coordinatorInstanceId)
            return ChangeUsernameResult.UserNotFound;

        newUsername = newUsername.Trim();
        if (user.Username == newUsername)
            return ChangeUsernameResult.NoError;

        // Check and rename under one lock, so two users cannot take the same free name at once
        await _renameLock.WaitAsync();
        try
        {
            var takenByConnected = _context.AttachedUsers.Values
                .Any(other => other.Id != userId && other.Username == newUsername);
            if (takenByConnected || await _userRepository.IsUsernameTakenAsync(newUsername, userId))
                return ChangeUsernameResult.UsernameAlreadyUsed;

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
        return ChangeUsernameResult.NoError;
    }
        
    public async Task<SetAvatarResult> SetAvatar(int userId, string? avatarUrl)
    {
        if (!_context.AttachedUsers.TryGetValue(userId, out var user) || user.IsSystem)
            return SetAvatarResult.UserNotFound;

        if (user.AvatarUrl == avatarUrl)
            return SetAvatarResult.NoError;

        user.SetAvatar(avatarUrl);
        await _userRepository.UpdateAsync(user);

        _logger.LogInformation("User {Id} changed the avatar.", userId);
        UserUpdated?.Invoke(user);
        return SetAvatarResult.NoError;
    }

    public async Task<SetUserStatusResult> SetStatus(int userId, UserStatus status)
    {
        if (status == UserStatus.Offline || !Enum.IsDefined(status))
            return SetUserStatusResult.InvalidStatus;

        if (!_context.AttachedUsers.TryGetValue(userId, out var user) || user.IsSystem)
            return SetUserStatusResult.UserNotFound;

        if (user.Status == status)
            return SetUserStatusResult.NoError;

        user.SetStatus(status);
        await _userRepository.UpdateAsync(user);

        _logger.LogTrace("User {Id} is now {Status}.", userId, status);
        UserUpdated?.Invoke(user);
        return SetUserStatusResult.NoError;
    }

    public async Task<ModerateUserResult> SetState(int userId, UserState state)
    {
        if (state is not (UserState.Normal or UserState.Suspended or UserState.Banned))
            return ModerateUserResult.InvalidState;

        var user = await GetUserAsync(userId);
        if (user == null || user.IsSystem || user.CoordinatorInstanceId != _coordinatorInstanceId)
            return ModerateUserResult.UserNotFound;

        if (user.State == state)
            return ModerateUserResult.NoError;

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
        return ModerateUserResult.NoError;
    }

    public async Task<ModerateUserResult> DeleteUser(int userId)
    {
        var user = await GetUserAsync(userId);
        if (user == null || user.IsSystem || user.CoordinatorInstanceId != _coordinatorInstanceId)
            return ModerateUserResult.UserNotFound;

        await _userRepository.RemoveAsync(userId);
        DetachUser(userId, "User is deleted");

        _logger.LogInformation("User {Id} ({Name}) deleted.", userId, user.Username);
        UserDeleted?.Invoke(userId);
        return ModerateUserResult.NoError;
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

    public void SetRoles(int userId, IEnumerable<int> roleIds)
    {
        if (!_context.AttachedUsers.TryGetValue(userId, out var user) || user.IsSystem)
            return;

        user.SetRoles(roleIds);
        UserUpdated?.Invoke(user);
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
        UserModel? user;
        lock (_attachLock)
        {
            if (connection.UserId is not { } userId
                || !_connections.UserConnections.TryGetValue(userId, out var connections)
                || !connections.Contains(connection.ConnectionId))
                return false;

            // Still connected from another device
            if (connections.Length > 1)
            {
                _connections.UserConnections[userId] = connections.Where(id => id != connection.ConnectionId).ToArray();
                return false;
            }

            _connections.UserConnections.TryRemove(userId, out _);
            if (!_context.AttachedUsers.TryRemove(userId, out user))
                return false;
        }

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
    private async Task<UserModel?> CreateUserAsync(string userIdentity, IClientConnection clientConnection)
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
                _nameProvider.GenerateBaseName(namePattern, takenNames), clientConnection.IpEndpoint, DateTime.UtcNow);

            newUser.Id = await _userRepository.AddAsync(newUser, userIdentity);
            return newUser;
        }
        finally
        {
            _createUserLock.Release();
        }
    }

    private bool IsOnlineLimitReached(int? exceptUserId)
    {
        var maxOnlineUsers = _coordinatorContext?.Model.MaxOnlineUsers ?? CoordinatorInstanceModel.NoUserLimit;
        if (maxOnlineUsers == CoordinatorInstanceModel.NoUserLimit)
            return false;

        var online = _context.AttachedUsers.Values.Count(user => !user.IsSystem && user.Id != exceptUserId);
        return online >= maxOnlineUsers;
    }

    private static bool IsProbableId(string value)
    {
        // crude check: GUIDs in this project are 36 chars with hyphens, db ids may be 36
        return !string.IsNullOrEmpty(value) && value.Length == 36 && value.Contains('-');
    }
}