using System.ComponentModel.DataAnnotations;
using Shared.Enums;

namespace Server.Infrastructure.Database.Entities;

public class RoleEntity
{
    public int Id { get; set; }

    [MaxLength(64)]
    public required string CoordinatorInstanceId { get; set; }

    [MaxLength(32)]
    public required string Name { get; set; }

    public int? Color { get; set; }
    public int Position { get; set; }
    public Permission Permissions { get; set; }
    public bool IsEveryone { get; set; }
    public long Revision { get; set; }
}

public class UserRoleEntity
{
    public int UserId { get; set; }
    public int RoleId { get; set; }
}
