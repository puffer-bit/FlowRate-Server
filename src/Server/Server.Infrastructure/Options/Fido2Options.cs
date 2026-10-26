using System.ComponentModel.DataAnnotations;

namespace Server.Infrastructure.Options;

/// <summary>
/// The WebAuthn relying party. Passkeys are bound to <see cref="RelyingPartyId"/>, the domain users reach the
/// server by: changing it later invalidates every registered passkey. Left empty, passkeys are not offered.
/// </summary>
public class Fido2Options : IValidatableObject
{
    public const string SectionName = "Fido2";

    /// <summary>A domain, without scheme or port ("chat.example.com", or "localhost" for development).</summary>
    public string? RelyingPartyId { get; set; }

    public string RelyingPartyName { get; set; } = "FlowRate";

    /// <summary>
    /// Origins a ceremony may come from ("https://chat.example.com:7114"): the control panel, where passkeys are
    /// registered, and the clients. Empty allows the control panel alone: https://RelyingPartyId:OrchestratorPort.
    /// </summary>
    public List<string> Origins { get; set; } = [];

    public bool IsEnabled => !string.IsNullOrWhiteSpace(RelyingPartyId);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (IsEnabled && (RelyingPartyId!.Contains("://") || RelyingPartyId.Contains('/') || RelyingPartyId.Contains(':')))
            yield return new ValidationResult("RelyingPartyId is a domain alone, without scheme, port or path.",
                [nameof(RelyingPartyId)]);

        foreach (var origin in Origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.AbsolutePath != "/")
                yield return new ValidationResult($"Origin '{origin}' is not a scheme, host and optional port.",
                    [nameof(Origins)]);
        }
    }
}
