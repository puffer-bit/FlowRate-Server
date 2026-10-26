using System.ComponentModel.DataAnnotations;
using Shared.Enums;

namespace Server.Infrastructure.Database.Entities;

public class PushDeviceEntity
{
    public int Id { get; set; }

    [MaxLength(64)]
    public required string CoordinatorInstanceId { get; set; }

    public int UserId { get; set; }

    [MaxLength(512)]
    public required string Token { get; set; }

    public PushPlatform Platform { get; set; }

    [MaxLength(32)]
    public required string KeyId { get; set; }

    [MaxLength(32)]
    public required byte[] Key { get; set; }

    [MaxLength(32)]
    public byte[]? ReplyTokenHash { get; set; }

    public DateTime UpdatedTime { get; set; }
}
