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

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!IPAddress.TryParse(ListenIp, out _))
            yield return new ValidationResult($"'{ListenIp}' is not a valid IP address.", [nameof(ListenIp)]);
    }
}
