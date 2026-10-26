using System.ComponentModel.DataAnnotations;

namespace Server.Infrastructure.Options;

/// <summary>"Storage" section. Everything the server keeps lives in one directory.</summary>
public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>A relative path is resolved against the working directory.</summary>
    [Required]
    public string Directory { get; set; } = "data";

    public string DatabaseConnectionString => $"Data Source={Path.Combine(Directory, "server.db")}";

    /// <summary>Where the self-signed certificate goes when none is configured.</summary>
    public string CertificatesDirectory => Path.Combine(Directory, "certs");

    public string MediaDirectory => Path.Combine(Directory, "media");
}
