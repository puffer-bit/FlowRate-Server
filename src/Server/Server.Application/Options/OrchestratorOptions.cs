using System.ComponentModel.DataAnnotations;
using System.Net;

namespace Server.Application.Options;

/// <summary>
/// "Orchestrator" section. The address of the control panel, which has to be known before the database is read,
/// and the host of the SFU the empty database is seeded with.
/// </summary>
public class OrchestratorOptions : IValidatableObject
{
    public const string SectionName = "Orchestrator";

    [Required]
    public string ListenIp { get; set; } = "0.0.0.0";

    [Range(1, 65535)]
    public int Port { get; set; } = 7114;

    /// <summary>Host of the SFU next to the server, written to the database on the first launch only.</summary>
    [Required]
    public string LocalSfuHost { get; set; } = "127.0.0.1";

    /// <summary>Credentials used only when seeding the local SFU into an empty database.</summary>
    public string LocalSfuUsername { get; set; } = string.Empty;
    public string LocalSfuPassword { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!IPAddress.TryParse(ListenIp, out _))
            yield return new ValidationResult($"'{ListenIp}' is not a valid IP address.", [nameof(ListenIp)]);
        if ((string.IsNullOrEmpty(LocalSfuUsername) != string.IsNullOrEmpty(LocalSfuPassword)) ||
            LocalSfuUsername.Contains(':') || LocalSfuUsername.Trim() != LocalSfuUsername)
            yield return new ValidationResult("Local SFU credentials must both be set, and the username cannot contain a colon or surrounding whitespace.",
                [nameof(LocalSfuUsername), nameof(LocalSfuPassword)]);
    }
}
