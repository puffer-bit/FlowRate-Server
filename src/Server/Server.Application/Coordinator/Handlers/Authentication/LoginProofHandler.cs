using System.Buffers.Text;
using Server.Application.Abstractions.Security;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Messaging;
using Shared.Models.DataTransferObjects;
using Shared.Models.Requests.Auth;

namespace Server.Application.Coordinator.Handlers.Authentication;

/// <summary>Finishes a login with the user's password and/or one-time code.</summary>
public class LoginProofHandler(IAuthenticator authenticator, SecondStageAuthentication secondStage)
    : IMessageHandler<LoginProofRequest>
{
    public Task<HandleMessageResult> HandleMessage(LoginProofRequest? request, string connectionId)
    {
        if (request == null)
            return Task.FromResult(HandleMessageResult.ForbiddenRequest);

        return secondStage.HandleAsync(request, connectionId,
            login => login.Challenge.AcceptsCodes
                ? authenticator.CheckUserLoginData(login.Challenge.UserId, request.Password, request.TwoFactorCode)
                : Task.FromResult(false),
            (user, deviceId) => request.ToResponse(LoginReplies.ToDto(user), deviceId));
    }
}

/// <summary>
/// Finishes a login with a passkey. The binary fields of the assertion are base64url, as WebAuthn gives them.
/// </summary>
public class LoginPasskeyHandler(IAuthenticator authenticator, SecondStageAuthentication secondStage)
    : IMessageHandler<LoginPasskeyRequest>
{
    public Task<HandleMessageResult> HandleMessage(LoginPasskeyRequest? request, string connectionId)
    {
        if (request == null)
            return Task.FromResult(HandleMessageResult.ForbiddenRequest);

        return secondStage.HandleAsync(request, connectionId,
            login => login.Challenge.Passkey is { } options && TryDecode(request.Assertion) is { } assertion
                ? authenticator.CheckPasskeyAsync(login.Challenge.UserId, options.OptionsJson, assertion)
                : Task.FromResult(false),
            (user, deviceId) => request.ToResponse(LoginReplies.ToDto(user), deviceId));
    }

    public static PasskeyAssertion? TryDecode(FIDO2AssertionDTO? dto)
    {
        if (dto == null)
            return null;

        var credentialId = Decode(dto.CredentialId);
        var clientData = Decode(dto.ClientDataJson);
        var authenticatorData = Decode(dto.AuthenticatorData);
        var signature = Decode(dto.Signature);
        if (credentialId == null || clientData == null || authenticatorData == null || signature == null)
            return null;

        var userHandle = string.IsNullOrEmpty(dto.UserHandle) ? null : Decode(dto.UserHandle);
        if (!string.IsNullOrEmpty(dto.UserHandle) && userHandle == null)
            return null;

        return new PasskeyAssertion(credentialId, clientData, authenticatorData, signature, userHandle);
    }

    private static byte[]? Decode(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        // Standard base64 with padding is taken too: clients differ in what they send
        var normalized = value.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        try
        {
            return Base64Url.DecodeFromChars(normalized);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
