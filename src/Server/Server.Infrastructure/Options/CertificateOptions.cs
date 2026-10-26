using System.ComponentModel.DataAnnotations;

namespace Server.Infrastructure.Options;

/// <summary>"Certificate" section. One certificate for the control panel (https) and the coordinators (wss).</summary>
public class CertificateOptions : IValidatableObject
{
    public const string SectionName = "Certificate";

    public string? Path { get; set; }

    public string? KeyPath { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Path);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Path) != string.IsNullOrWhiteSpace(KeyPath))
            yield return new ValidationResult("Path and KeyPath must be set together, or both left empty " +
                "to use a generated self-signed certificate.", [nameof(Path), nameof(KeyPath)]);
    }
}
