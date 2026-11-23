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
    /// <summary>The coordinator has no such user.</summary>
    UnknownUser,
    /// <summary>The connection closed before the user was attached.</summary>
    ConnectionClosed,
    /// <summary>The user already has five connections open.</summary>
    ConnectionLimitReached
}

/// <param name="User">Set when the user was attached.</param>
public sealed record UserAttachment(AttachUserResult Result, UserModel? User = null)
{
    public static UserAttachment Rejected(AttachUserResult result) => new(result);
}
