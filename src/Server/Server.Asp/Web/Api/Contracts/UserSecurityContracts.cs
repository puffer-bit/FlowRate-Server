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

public sealed record PasskeyDto(int Id, string Label, DateTime CreatedAtUtc, DateTime? LastUsedAtUtc, bool IsEnabled);

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
