using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Messaging;
using Server.Application.Mappers;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests;

namespace Server.Application.Coordinator.Handlers.Authentication;

/// <summary>What the login handlers share: answering, and closing the connection of a login that failed.</summary>
public sealed class LoginReplies(IMessageSender messageSender, IConnectionManager connectionManager)
{
    /// <summary>The connection, unless it is gone or logged in already (which is answered and left open).</summary>
    public IClientConnection? LoggedOutConnection(Request request, string connectionId)
    {
        if (!connectionManager.TryGetConnectionById(connectionId, out var connection))
            return null;
        if (connection.UserId == null)
            return connection;

        messageSender.SendMessageById(connectionId, request.ToError(ErrorCode.AuthFailed));
        return null;
    }

    public HandleMessageResult Respond(Request request, IClientConnection connection, LoginOutcome outcome,
        Func<Server.Core.Models.User.UserModel, Guid, BaseMessage> response)
    {
        if (outcome.Error is { } error)
            return Reject(request, connection, error, outcome.BanReason);

        var user = outcome.User!;
        messageSender.SendMessageById(connection.ConnectionId, response(user, outcome.DeviceId!.Value));
        return HandleMessageResult.NoError;
    }

    /// <summary>Answers the error and closes the connection: a failed login starts over on a new one.</summary>
    public HandleMessageResult Reject(Request? request, IClientConnection connection,
        ErrorCode error = ErrorCode.AuthFailed, string? details = null)
    {
        if (request != null)
            messageSender.SendMessageById(connection.ConnectionId, request.ToError(error, details));
        connection.Close();
        return HandleMessageResult.ForbiddenRequest;
    }

    public void Send(string connectionId, BaseMessage message) => messageSender.SendMessageById(connectionId, message);

    public static Shared.Models.DataTransferObjects.UserDTO ToDto(Server.Core.Models.User.UserModel user) =>
        user.ToDto(user.Id);
}
