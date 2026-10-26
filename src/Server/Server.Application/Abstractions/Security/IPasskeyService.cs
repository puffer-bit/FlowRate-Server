namespace Server.Application.Abstractions.Security;

/// <summary>
/// The WebAuthn (FIDO2) ceremonies. Options go out as the JSON the browser API takes, and come back unchanged
/// to finish the ceremony they started, so nothing about them has to be stored but that string.
/// </summary>
public interface IPasskeyService
{
    /// <summary>False when no relying party is configured; passkeys cannot be registered or checked then.</summary>
    bool IsAvailable { get; }

    /// <summary>Options for navigator.credentials.create. Known credentials of the user are excluded.</summary>
    string BeginRegistration(int userId, string userName, IReadOnlyList<byte[]> knownCredentialIds);

    /// <summary>Checks a new credential against the options it was made for; null when it does not hold.</summary>
    Task<PasskeyRegistration?> FinishRegistrationAsync(string optionsJson, string attestationResponseJson,
        Func<byte[], Task<bool>> isCredentialIdFree, CancellationToken ct = default);

    /// <summary>Options for navigator.credentials.get, limited to these credentials.</summary>
    PasskeyLoginOptions BeginLogin(IReadOnlyList<byte[]> allowedCredentialIds);

    /// <summary>
    /// Checks an assertion against the options it answers and the stored key of the credential; null when it does
    /// not hold, else the new signature counter.
    /// </summary>
    Task<uint?> VerifyLoginAsync(string optionsJson, PasskeyAssertion assertion, byte[] storedPublicKey,
        uint storedSignatureCounter, int userId, CancellationToken ct = default);
}

public static class PasskeyUserHandle
{
    /// <summary>The user handle the credentials of this user carry: the user id as text.</summary>
    public static byte[] Of(int userId) => System.Text.Encoding.UTF8.GetBytes(userId.ToString());
}

public sealed record PasskeyRegistration(byte[] CredentialId, byte[] PublicKey, uint SignatureCounter, Guid AaGuid);

/// <param name="OptionsJson">The whole options, kept to finish the login with.</param>
/// <param name="Challenge">Base64url, as the other binary values here.</param>
public sealed record PasskeyLoginOptions(
    string OptionsJson,
    string Challenge,
    string RelyingPartyId,
    ulong TimeoutMs,
    IReadOnlyList<string> AllowedCredentialIds,
    string UserVerification);

public sealed record PasskeyAssertion(
    byte[] CredentialId,
    byte[] ClientDataJson,
    byte[] AuthenticatorData,
    byte[] Signature,
    byte[]? UserHandle);
