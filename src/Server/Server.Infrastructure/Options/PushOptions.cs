using System.ComponentModel.DataAnnotations;

namespace Server.Infrastructure.Options;

public class PushOptions : IValidatableObject
{
    public const string SectionName = "Push";

    public string? ServiceAccountPath { get; set; }

    public string? RelayUrl { get; set; }

    public bool SendsDirectly => !string.IsNullOrWhiteSpace(ServiceAccountPath);

    public bool SendsThroughRelay => !SendsDirectly && !string.IsNullOrWhiteSpace(RelayUrl);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SendsDirectly && !File.Exists(ServiceAccountPath))
            yield return new ValidationResult($"The service account key '{ServiceAccountPath}' does not exist.",
                [nameof(ServiceAccountPath)]);

        if (!string.IsNullOrWhiteSpace(RelayUrl)
            && !(Uri.TryCreate(RelayUrl, UriKind.Absolute, out var uri)
                 && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)))
            yield return new ValidationResult("RelayUrl must be an absolute http or https URL.", [nameof(RelayUrl)]);
    }
}
