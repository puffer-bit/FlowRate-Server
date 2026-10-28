using System.ComponentModel.DataAnnotations;

namespace Server.Infrastructure.Database.Entities;

public class UserSessionEntity
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public required string CoordinatorInstanceId { get; set; }

    public int UserId { get; set; }

    [MaxLength(32)]
    public required byte[] TokenHash { get; set; }

    [MaxLength(32)]
    public byte[]? PreviousTokenHash { get; set; }

    [MaxLength(64)]
    public required string DeviceName { get; set; }

    [MaxLength(32)]
    public required string DevicePlatform { get; set; }

    [MaxLength(32)]
    public required string AppVersion { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    [MaxLength(45)]
    public string? LastAddress { get; set; }
}
