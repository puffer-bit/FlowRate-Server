using System.ComponentModel.DataAnnotations;

namespace Server.Database.Entities;

/// <summary>A one-time login code the control panel gave out for a user; only its hash is kept.</summary>
public class UserLoginCodeEntity
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public required string CoordinatorInstanceId { get; set; }

    public int UserId { get; set; }

    [MaxLength(32)]
    public required byte[] CodeHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
