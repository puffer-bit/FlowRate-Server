using System.ComponentModel.DataAnnotations;

namespace Server.PushRelay;

public class RelayOptions : IValidatableObject
{
    public const string SectionName = "Relay";

    [Required]
    public string ServiceAccountPath { get; set; } = "";

    [Range(1, 1_000_000)]
    public int RequestsPerMinutePerAddress { get; set; } = 600;

    [Range(1, 1_000_000)]
    public int RequestsPerMinutePerToken { get; set; } = 60;

    public bool TrustForwardedHeaders { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(ServiceAccountPath) && !File.Exists(ServiceAccountPath))
            yield return new ValidationResult($"The service account key '{ServiceAccountPath}' does not exist.",
                [nameof(ServiceAccountPath)]);
    }
}
