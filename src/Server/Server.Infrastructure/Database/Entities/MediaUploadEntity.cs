using System.ComponentModel.DataAnnotations;
using Server.Core.Models.Media;

namespace Server.Infrastructure.Database.Entities;

public class MediaUploadEntity
{
    [MaxLength(32)]
    public required string Id { get; set; }

    [MaxLength(36)]
    public required string CoordinatorInstanceId { get; set; }

    public int OwnerUserId { get; set; }
    public int? ChatId { get; set; }
    public MediaUploadKind Kind { get; set; }

    [MaxLength(100)]
    public required string MimeType { get; set; }

    public long Size { get; set; }
    public MediaUploadState State { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? MessageId { get; set; }
}
