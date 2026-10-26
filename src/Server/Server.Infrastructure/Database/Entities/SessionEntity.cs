using System.ComponentModel.DataAnnotations;
using Shared.Enums;
using Shared.Enums.WebRTC;

namespace Server.Infrastructure.Database.Entities;

public class SessionEntity
{
    [MaxLength(36)]
    public required int Id { get; set; }
        
    [MaxLength(50)]
    public required string Name { get; set; }
    
    [MaxLength(36)]
    public required string CoordinatorInstanceId { get; set; }
    
    public int Capacity { get; set; }
}
/// <summary>What one role allows and denies in one session.</summary>
public class SessionPermissionOverrideEntity
{
    public int SessionId { get; set; }
    public int RoleId { get; set; }
    public Permission Allow { get; set; }
    public Permission Deny { get; set; }
}
