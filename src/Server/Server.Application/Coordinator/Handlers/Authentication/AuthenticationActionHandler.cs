using Microsoft.Extensions.Logging;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Messaging;
using Server.Application.Mappers;
using Shared.Enums;
using Shared.Enums.Auth;
using Shared.Models.DataTransferObjects;
using Shared.Models.Requests.Auth;
using Shared.Models.Responses.Auth;

namespace Server.Application.Coordinator.Handlers.Authentication;

/// <summary>
/// The first login stage. A device with the token of a session the server knows is let in at once. Otherwise a user
/// who has a password, one-time codes or passkeys is answered
/// <see cref="AuthResult.SecondStageRequeried"/> and finishes with <see cref="TwoFactorUserAuthRequest"/> or
/// <see cref="FIDO2UserAuthRequest"/>; everyone else is let in at once.
/// </summary>
public class AuthenticationActionHandler : IMessageHandler<UserAuthRequest>
{
    private readonly ILogger _logger;
    private readonly IMessageSender _messageSender;
    private readonly IAuthenticator _authenticator;
    private readonly IConnectionManager _connectionManager;
    private readonly UserLogin _userLogin;
    private readonly PendingLogins _pendingLogins;

    public AuthenticationActionHandler(
        ILogger logger,
        IMessageSender messageSender,
        IAuthenticator authenticator,
        IConnectionManager connectionManager,
        UserLogin userLogin,
        PendingLogins pendingLogins)
    {
        _logger = logger;
        _messageSender = messageSender;
        _authenticator = authenticator;
        _connectionManager = connectionManager;
        _userLogin = userLogin;
        _pendingLogins = pendingLogins;
    }

    public async Task<HandleMessageResult> HandleMessage(UserAuthRequest? request, string connectionId)
    {
        if (!_connectionManager.TryGetConnectionById(connectionId, out var clientConnection))
        {
            _logger.LogTrace("User auth failed for connection {ConnectionId}. Failed to find connection", connectionId);
            return HandleMessageResult.GatewayNotFound;
        }

        // A connection that is logged in already stays with its user; it is not closed for asking
        if (clientConnection.UserId != null)
        {
            if (request != null)
                _messageSender.SendMessageById(connectionId, request.ToResponse(AuthResult.AuthFailed));
            return HandleMessageResult.ForbiddenRequest;
        }

        try
        {
            if (request == null || request.UserIdentity == "system" ||
                !_authenticator.CheckLoginData(request.ServerPassword))
            {
                Reject(request, clientConnection);
                return HandleMessageResult.ForbiddenRequest;
            }

            // A token that is unknown or expired is not refused: the device goes on with its identity
            if (request.SessionToken is { } token && await _userLogin.Sessions.FindAsync(token) is { } session)
                return Respond(request, clientConnection,
                    await _userLogin.ResumeAsync(session, token, request.Device, clientConnection));

            if (request.UserIdentity is { } identity
                && await _authenticator.BeginUserLoginAsync(identity) is { } challenge)
            {
                _pendingLogins.Start(connectionId, identity, request.Device, challenge);
                _messageSender.SendMessageById(connectionId, new UserAuthResponse(request.RequestId, null, null,
                    AuthResult.SecondStageRequeried, ToServerMessage(challenge), ToDto(challenge.Passkey)));
                return HandleMessageResult.NoError;
            }

            return Respond(request, clientConnection,
                await _userLogin.AttachAsync(request.UserIdentity, request.Device, clientConnection));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "User auth failed for connection {ConnectionId}", connectionId);
            Reject(request, clientConnection);
            return HandleMessageResult.InternalError;
        }
    }

    private HandleMessageResult Respond(UserAuthRequest request, IClientConnection clientConnection,
        LoginOutcome outcome)
    {
        if (outcome.Result != AuthResult.Success)
        {
            _logger.LogTrace("User auth rejected for connection {ConnectionId}: {Result}",
                clientConnection.ConnectionId, outcome.Attach);
            Reject(request, clientConnection, outcome.Result);
            return HandleMessageResult.ForbiddenRequest;
        }

        _messageSender.SendMessageById(clientConnection.ConnectionId, request.ToResponse(AuthResult.Success,
            outcome.UserIdentity, outcome.User!.ToDto(outcome.User.Id), sessionToken: outcome.SessionToken));
        return HandleMessageResult.NoError;
    }

    /// <summary>What the user can prove, as names of <see cref="SecondStageAuthType"/>: "Password,TwoFactor".</summary>
    private static string ToServerMessage(LoginChallenge challenge) => string.Join(',', challenge.Methods);

    private static FIDO2LoginOptionsDTO? ToDto(Abstractions.Security.PasskeyLoginOptions? options) =>
        options == null
            ? null
            : new FIDO2LoginOptionsDTO
            {
                Challenge = options.Challenge,
                RpId = options.RelyingPartyId,
                Timeout = (long)options.TimeoutMs,
                AllowedCredentials = options.AllowedCredentialIds
                    .Select(id => new FIDO2AllowedCredentialDto { Id = id })
                    .ToList(),
                UserVerification = options.UserVerification
            };

    private void Reject(UserAuthRequest? request, IClientConnection clientConnection,
        AuthResult result = AuthResult.AuthFailed)
    {
        if (request != null)
            _messageSender.SendMessageById(clientConnection.ConnectionId, request.ToResponse(result));
        clientConnection.Close();
    }
}
