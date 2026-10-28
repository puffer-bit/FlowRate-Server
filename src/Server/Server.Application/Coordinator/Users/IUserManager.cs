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
    /// Attaches the user and makes the connection theirs, unless the coordinator limits say no; see
    /// <see cref="AttachUserResult"/>. A user can be on several connections, one per device: a user connected
    /// already stays attached as they are, and <see cref="UserConnected"/> is raised only when they come online.
    /// </summary>
    Task<UserAttachment> AttachUser(string? userIdentity, IClientConnection clientConnection);

    /// <summary>Attaches the user of a device session, who proved themselves when the session started.</summary>
    Task<UserAttachment> AttachUser(int userId, IClientConnection clientConnection);
    Task<ErrorCode?> ChangeUsername(int userId, string newUsername);
    Task<ErrorCode?> SetAvatar(int userId, string? avatarUrl);

    /// <summary>Sets the status a connected user picked; announced as an update of the user.</summary>
    Task<ErrorCode?> SetStatus(int userId, UserStatus status);

    /// <summary>
    /// Bans, freezes or reinstates a user, connected or not; announced as an update of the user. Neither
    /// disconnects them.
    /// </summary>
    Task<ErrorCode?> SetState(int userId, UserState state);

    /// <summary>Deletes a user, detaching them when connected; their connection stays open.</summary>
    Task<ErrorCode?> DeleteUser(int userId);

    /// <summary>Takes the roles of a connected user saved elsewhere; announced as an update of the user.</summary>
    void SetRoles(int userId, IEnumerable<int> roleIds);
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