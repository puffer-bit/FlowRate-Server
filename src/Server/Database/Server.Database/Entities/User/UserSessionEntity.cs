using System.ComponentModel.DataAnnotations;

namespace Server.Database.Entities;

public class UserSessionEntity
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public required string CoordinatorInstanceId { get; set; }

    public int UserId { get; set; }

    /// <summary>The session token of the earlier protocol 3; null for devices with a key.</summary>
    [MaxLength(32)]
    public byte[]? TokenHash { get; set; }

    /// <summary>The P-256 public key of the device (SubjectPublicKeyInfo); null for sessions of the earlier protocol 3.</summary>
    [MaxLength(128)]
    public byte[]? PublicKey { get; set; }

    /// <summary>Not remembered by the client: the device ends after a day without a login.</summary>
    public bool IsTemporary { get; set; }

    /// <summary>How the device was added, a <c>DeviceLoginMethod</c>.</summary>
    public int AddedBy { get; set; }

    [MaxLength(32)]
    public byte[]? PreviousTokenHash { get; set; }

    [MaxLength(64)]
    public required string DeviceName { get; set; }

    [MaxLength(32)]
    public required string DevicePlatform { get; set; }

    [MaxLength(32)]
    public required string AppVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    [MaxLength(45)]
    public string? LastAddress { get; set; }
}
