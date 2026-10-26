using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Users;
using Server.Core.Models.User;
using Shared.Enums.Auth;

namespace Server.Application.Coordinator.Authentication;

/// <summary>
/// The last step of every login: the user is attached and the connection is theirs. The other connections of the
/// user stay: a user can be connected from several devices.
/// </summary>
public sealed class UserLogin(IUserManager userManager)
{
    public async Task<LoginOutcome> AttachAsync(string? userIdentity, IClientConnection connection)
    {
        var attachment = await userManager.AttachUser(userIdentity, connection);
        if (attachment is not { Result: AttachUserResult.Attached, User: { } user, UserIdentity: { } identity })
        {
            return new LoginOutcome(attachment.Result switch
            {
                AttachUserResult.OnlineLimitReached => AuthResult.OnlineLimitReached,
                AttachUserResult.UserLimitReached => AuthResult.UserLimitReached,
                AttachUserResult.Banned => AuthResult.Banned,
                _ => AuthResult.AuthFailed
            }, null, null, attachment.Result);
        }

        return new LoginOutcome(AuthResult.Success, user, identity, attachment.Result);
    }
}

public sealed record LoginOutcome(AuthResult Result, UserModel? User, string? UserIdentity, AttachUserResult Attach);
