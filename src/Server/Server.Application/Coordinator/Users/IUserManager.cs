using Shared.Enums;
using Server.Core.Models.User;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Events;
using Shared.Enums.User;

namespace Server.Application.Coordinator.Users;

public interface IUserManager
{
    event Action<UserModel> UserConnected;
    event Action<UserModel> UserUpdated;
    event Action<UserModel, string> UserDisconnected;
    event Action<int> UserDeleted;
    
    int ConnectedUsersCount { get; }

    /// <summary>
    /// Registers a new user with the recovery keys of the account and attaches them, unless the coordinator limits say
    /// no; see <see cref="AttachUserResult"/>.
    /// </summary>
    Task<UserAttachment> RegisterUser(UserRecoveryKeys recoveryKeys, IClientConnection clientConnection);

    /// <summary>
    /// Attaches a user who proved themselves and makes the connection theirs, unless the coordinator limits say no;
    /// see <see cref="AttachUserResult"/>. A user can be on several connections, one per device: a user connected
    /// already stays attached as they are, and <see cref="UserConnected"/> is raised only when they come online.
    /// </summary>
    Task<UserAttachment> AttachUser(int userId, IClientConnection clientConnection);
    Task<ErrorCode?> ChangeUsername(int userId, string newUsername);
    Task<ErrorCode?> SetAvatar(int userId, string? avatarUrl);

    /// <summary>Sets the status a connected user picked; announced as an update of the user.</summary>
    Task<ErrorCode?> SetStatus(int userId, UserStatus status);

    /// <summary>
    /// Sets how long the device on the connection has gone without input; announced as an update of the user when
    /// what others see changes. Not saved.
    /// </summary>
    ErrorCode? SetIdleLevel(int userId, string connectionId, IdleLevel level);

    /// <summary>
    /// Bans, freezes or reinstates a user, connected or not; announced as an update of the user. Neither
    /// disconnects them.
    /// </summary>
    Task<ErrorCode?> SetState(int userId, UserState state);

    /// <summary>
    /// Deletes a user, detaching them when connected; their connection stays open. Bots the user made lose their
    /// owner.
    /// </summary>
    Task<ErrorCode?> DeleteUser(int userId);

    /// <summary>
    /// Makes a bot owned by <paramref name="ownerUserId"/>, a debug bot with <paramref name="isDebug"/>: refused
    /// with <see cref="ErrorCode.NameTaken"/> or, past the limit of the coordinator (debug bots do not count),
    /// <see cref="ErrorCode.BotLimitReached"/>.
    /// </summary>
    Task<(ErrorCode? Error, UserModel? Bot)> CreateBot(int ownerUserId, string username, bool isDebug);

    /// <summary>The bots of the coordinator, the connected ones as they are attached.</summary>
    Task<IReadOnlyList<UserModel>> GetBotsAsync();

    /// <summary>
    /// Takes the roles of a user saved elsewhere; announced as an update of the user. A connected user takes them
    /// at once, before the task completes; one not connected is read as saved, so that the others see them too.
    /// </summary>
    Task SetRolesAsync(int userId, IEnumerable<int> roleIds);
    void DetachUser(int userId, string reason);
    /// <summary>
    /// Takes a closed connection from its user, and detaches the user when it was their last one.
    /// </summary>
    /// <returns>True when the user was detached.</returns>
    bool DetachConnection(IClientConnection connection, string reason);
    bool TryGetConnectedUser(int userId, out UserModel? user);
    Task<UserModel?> GetUserAsync(int userId);
    IReadOnlyDictionary<int, UserModel> GetAllConnectedUsers();
    /// <summary>All the users of the coordinator; the connected ones as they are attached.</summary>
    Task<IReadOnlyDictionary<int, UserModel>> GetAllUsersAsync();
    void ForceDetachAllUsers();
}