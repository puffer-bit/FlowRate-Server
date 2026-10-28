using Microsoft.Extensions.Logging;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Messaging;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests;

namespace Server.Application.Coordinator.Handlers.Authentication;

/// <summary>
/// What both second-stage handlers share: find the login the connection started, let the user in when the proof
/// holds, and close the connection after too many wrong answers.
/// </summary>
public sealed class SecondStageAuthentication(
    ILogger logger,
    IMessageSender messageSender,
    IConnectionManager connectionManager,
    UserLogin userLogin,
    PendingLogins pendingLogins)
{
    /// <param name="respond">The response to the request once the user is in, with the next session token.</param>
    public async Task<HandleMessageResult> HandleAsync(Request request, string connectionId,
        Func<PendingLogin, Task<bool>> proves,
        Func<UserModel, string, BaseMessage> respond)
    {
        if (!connectionManager.TryGetConnectionById(connectionId, out var connection))
            return HandleMessageResult.GatewayNotFound;

        // A connection that is logged in already has nothing to finish; it is not closed for asking
        if (connection.UserId != null)
        {
            messageSender.SendMessageById(connectionId, request.ToError(ErrorCode.AuthFailed));
            return HandleMessageResult.ForbiddenRequest;
        }

        var login = pendingLogins.Get(connectionId);
        if (login == null)
        {
            Reject(connection, request.ToError(ErrorCode.AuthFailed));
            return HandleMessageResult.ForbiddenRequest;
        }

        bool proven;
        try
        {
            proven = await proves(login);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Checking the second login stage of user {UserId} failed.", login.Challenge.UserId);
            pendingLogins.Complete(connectionId);
            Reject(connection, request.ToError(ErrorCode.AuthFailed));
            return HandleMessageResult.InternalError;
        }

        if (!proven)
        {
            logger.LogInformation("Wrong second login stage for user {UserId} from {Ip}.", login.Challenge.UserId,
                connection.IpEndpoint);
            if (pendingLogins.RegisterFailure(connectionId))
                Reject(connection, request.ToError(ErrorCode.AuthFailed));
            else
                messageSender.SendMessageById(connectionId, request.ToError(ErrorCode.AuthFailed));
            return HandleMessageResult.ForbiddenRequest;
        }

        pendingLogins.Complete(connectionId);
        var outcome = await userLogin.AttachAsync(login.UserIdentity, login.Device, connection);
        if (outcome.Error is { } error)
        {
            Reject(connection, request.ToError(error));
            return HandleMessageResult.ForbiddenRequest;
        }

        messageSender.SendMessageById(connectionId, respond(outcome.User!, outcome.SessionToken!));
        return HandleMessageResult.NoError;
    }

    private void Reject(IClientConnection connection, BaseMessage response)
    {
        messageSender.SendMessageById(connection.ConnectionId, response);
        connection.Close();
    }
}
