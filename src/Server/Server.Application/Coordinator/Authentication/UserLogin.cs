using Shared.Enums;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Users;
using Server.Core.Models.User;
using Shared.Enums.Auth;
using Shared.Models.DataTransferObjects;

namespace Server.Application.Coordinator.Authentication;

/// <summary>
/// The last step of every login: the user is attached, the connection is theirs and the device gets the token of its
/// session. The other connections of the user stay: a user can be connected from several devices.
/// </summary>
public sealed class UserLogin(IUserManager userManager, UserSessions sessions)
{
    public UserSessions Sessions => sessions;

    /// <summary>A login with the identity, proven by the second stage when the user has one: a new session.</summary>
    public async Task<LoginOutcome> AttachAsync(string? userIdentity, DeviceInfoDTO? device,
        IClientConnection connection)
    {
        var attachment = await userManager.AttachUser(userIdentity, connection);
        if (Rejected(attachment) is { } rejected)
            return rejected;

        var token = await sessions.StartAsync(attachment.User!.Id, device, connection);
        return new LoginOutcome(null, attachment.User, attachment.UserIdentity, attachment.Result, token);
    }

    /// <summary>A login with the token of a session: the session goes on with the next token.</summary>
    public async Task<LoginOutcome> ResumeAsync(UserSession session, string token, DeviceInfoDTO? device,
        IClientConnection connection)
    {
        var attachment = await userManager.AttachUser(session.UserId, connection);
        if (Rejected(attachment) is { } rejected)
            return rejected;

        var nextToken = await sessions.ResumeAsync(session, token, device, connection);
        return new LoginOutcome(null, attachment.User, null, attachment.Result, nextToken);
    }

    private static LoginOutcome? Rejected(UserAttachment attachment) =>
        attachment is { Result: AttachUserResult.Attached, User: not null }
            ? null
            : new LoginOutcome(attachment.Result switch
            {
                AttachUserResult.OnlineLimitReached => ErrorCode.OnlineLimitReached,
                AttachUserResult.UserLimitReached => ErrorCode.UserLimitReached,
                AttachUserResult.Banned => ErrorCode.Banned,
                _ => ErrorCode.AuthFailed
            }, null, null, attachment.Result, null);
}

/// <param name="Error">Why the user was not let in; null when it was.</param>
/// <param name="UserIdentity">The identity the user logged in with, or got; null for a login with a session token.</param>
/// <param name="SessionToken">The token the device logs in with next time.</param>
public sealed record LoginOutcome(ErrorCode? Error, UserModel? User, string? UserIdentity, AttachUserResult Attach,
    string? SessionToken);
