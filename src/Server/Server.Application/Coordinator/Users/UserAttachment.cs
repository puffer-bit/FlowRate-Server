using Server.Core.Models.User;

namespace Server.Application.Coordinator.Users;

public enum AttachUserResult
{
    Attached,
    /// <summary>The coordinator already has <c>MaxOnlineUsers</c> users connected.</summary>
    OnlineLimitReached,
    /// <summary>The user is new, and the coordinator already has <c>MaxUsers</c> users registered.</summary>
    UserLimitReached,
    /// <summary>The user is banned from the coordinator.</summary>
    Banned,
    /// <summary>No user of the coordinator has the identity given.</summary>
    UnknownIdentity,
    /// <summary>The connection closed before the user was attached.</summary>
    ConnectionClosed
}

/// <param name="User">Set when the user was attached.</param>
/// <param name="UserIdentity">Set when the user was attached: the identity the client has to keep.</param>
public sealed record UserAttachment(AttachUserResult Result, UserModel? User = null, string? UserIdentity = null)
{
    public static UserAttachment Rejected(AttachUserResult result) => new(result);
}
