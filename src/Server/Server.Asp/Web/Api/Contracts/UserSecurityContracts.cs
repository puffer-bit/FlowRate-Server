using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Server.Asp.Web.Api.Contracts;

/// <summary>How a user proves who they are when logging in. Secrets are never returned.</summary>
/// <param name="IsTwoFactorPending">A secret was offered and waits for its first code.</param>
/// <param name="PasskeysAvailable">False when the server has no Fido2:RelyingPartyId; passkeys cannot be added.</param>
public sealed record UserSecurityDto(
    bool HasPassword,
    bool IsTwoFactorEnabled,
    bool IsTwoFactorPending,
    bool PasskeysAvailable,
    IReadOnlyList<PasskeyDto> Passkeys);

public sealed record PasskeyDto(int Id, string Label, DateTimeOffset CreatedAtUtc, DateTimeOffset? LastUsedAtUtc, bool IsEnabled);

public sealed class SetPasswordDto
{
    /// <summary>8 to 128 characters; null removes the password.</summary>
    public string? Password { get; init; }
}

/// <param name="Secret">Base32, to type into an authenticator app.</param>
/// <param name="Uri">otpauth:// URI, for a QR code.</param>
public sealed record TwoFactorOfferDto(string Secret, string Uri);

public sealed class TwoFactorCodeDto
{
    [Required]
    public string Code { get; init; } = string.Empty;
}

public sealed class RegisterPasskeyDto
{
    public string? Label { get; init; }

    /// <summary>The PublicKeyCredential navigator.credentials.create gave, as JSON (toJSON()).</summary>
    [Required]
    public JsonElement Credential { get; init; }
}

/// <summary>A device of a user's account.</summary>
/// <param name="AddedBy">How the device was added: registration, recovery phrase, factors, login code, another device.</param>
/// <param name="IsOnline">Connected now; false while the coordinator is stopped.</param>
public sealed record UserDeviceDto(
    Guid Id,
    string Name,
    string Platform,
    string AppVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastUsedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string? LastAddress,
    bool IsTemporary,
    Shared.Enums.Auth.DeviceLoginMethod AddedBy,
    bool IsOnline);

/// <param name="Code">Shown once; the user types it into "Log in to an existing account" on the new device.</param>
/// <param name="Port">The port of the coordinator, for the link that opens the client on the code.</param>
/// <param name="CertificateFingerprint">
/// SHA-256 of the certificate of the server in hex, for the link: the client trusts it without asking when the system
/// cannot verify it.
/// </param>
public sealed record LoginCodeDto(string Code, DateTimeOffset ExpiresAtUtc, int? Port, string CertificateFingerprint);
