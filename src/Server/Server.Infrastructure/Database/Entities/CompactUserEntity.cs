using Microsoft.EntityFrameworkCore;
using Shared.Enums.User;

namespace Server.Infrastructure.Database.Entities;

[Owned]
public class CompactUserEntity
{
    public int Id { get; set; }
    public string CoordinatorInstanceId { get; set; }
    public string Username { get; set; }
    public string? DisplayName { get; set; }
    public string? Prefix { get; set; }
    public string? AvatarUrl { get; set; }
    public UserState State { get; set; }
}