using Microsoft.Extensions.Logging;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Messaging;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Enums.Auth;
using Shared.Models;
using Shared.Models.Requests;

namespace Server.Application.Coordinator.Handlers.Authentication;

/// <summary>
/// What both second-stage handlers share: find the login the connection started, add the device when the proof holds,
/// and close the connection after too many wrong answers.
/// </summary>
public sealed class SecondStageAuthentication(
    ILogger logger,
    LoginReplies replies,
    UserLogin userLogin,
    PendingLogins pendingLogins,
    IUserRepository users)
{
    /// <param name="respond">The response to the request once the user is in, with the device to log in with.</param>
    public async Task<HandleMessageResult> HandleAsync(Request request, string connectionId,
        Func<PendingLogin, Task<bool>> proves,
        Func<UserModel, Guid, BaseMessage> respond)
    {
        if (replies.LoggedOutConnection(request, connectionId) is not { } connection)
            return HandleMessageResult.ForbiddenRequest;

        var login = pendingLogins.Get(connectionId);
        if (login == null)
            return replies.Reject(request, connection);

        bool proven;
        try
        {
            proven = await proves(login);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Checking the second login stage of user {UserId} failed.", login.Challenge.UserId);
            pendingLogins.Complete(connectionId);
            return replies.Reject(request, connection, ErrorCode.Internal);
        }

        if (!proven)
        {
            logger.LogInformation("Wrong second login stage for user {UserId} from {Ip}.", login.Challenge.UserId,
                connection.IpEndpoint);
            if (pendingLogins.RegisterFailure(connectionId))
                return replies.Reject(request, connection);

            replies.Send(connectionId, request.ToError(ErrorCode.AuthFailed));
            return HandleMessageResult.ForbiddenRequest;
        }

        pendingLogins.Complete(connectionId);
        var userId = login.Challenge.UserId;
        var outcome = login is { Method: DeviceLoginMethod.Legacy, LegacyKeys: { } keys }
            ? await LegacyAccounts.MoveOverAsync(userLogin, users, userId, keys, login.Device, login.Info, connection)
            : await userLogin.AddDeviceAsync(userId, login.Device, login.Info, login.Method, connection);
        return replies.Respond(request, connection, outcome, respond);
    }
}
