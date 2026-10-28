using System.Buffers.Text;
using Server.Application.Abstractions.Security;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Messaging;
using Server.Application.Mappers;
using Shared.Enums;
using Shared.Models.DataTransferObjects;
using Shared.Models.Requests.Auth;

namespace Server.Application.Coordinator.Handlers.Authentication;

/// <summary>
/// Finishes a login with a passkey. The binary fields of the assertion are base64url, as WebAuthn gives them.
/// </summary>
public class Fido2AuthenticationHandler(IAuthenticator authenticator, SecondStageAuthentication secondStage)
    : IMessageHandler<FIDO2UserAuthRequest>
{
    public Task<HandleMessageResult> HandleMessage(FIDO2UserAuthRequest? request, string connectionId)
    {
        if (request == null)
            return Task.FromResult(HandleMessageResult.ForbiddenRequest);

        return secondStage.HandleAsync(connectionId,
            login => login.Challenge.Passkey is { } options && TryDecode(request.AssertionResponse) is { } assertion
                ? authenticator.CheckPasskeyAsync(login.Challenge.UserId, options.OptionsJson, assertion)
                : Task.FromResult(false),
            (result, user, token) => request.ToResponse(result, user?.ToDto(user.Id), sessionToken: token));
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
