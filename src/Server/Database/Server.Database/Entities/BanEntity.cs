using Shared.Enums.User;

namespace Server.Database.Entities;

public class BanEntity
{
    public Guid Id { get; set; }
    public required string CoordinatorId { get; set; }
    public BanKind Kind { get; set; }
    public int? UserId { get; set; }
    public string? Address { get; set; }
    public required string Reason { get; set; }
    public int CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public UserState PreviousState { get; set; }
}
