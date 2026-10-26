using System.ComponentModel.DataAnnotations;
using Shared.Models.DataTransferObjects.ChatSession.Messages;

namespace Server.Infrastructure.Database.Entities.TextChat;

public class ChatImageContentEntity
{
    [MaxLength(36)]
    public required int Id { get; set; }
    
    // foreign key back to the parent message
    [MaxLength(36)]
    public required int MessageId { get; set; }

    public required string FileName { get; set; }
    public string? Thumbnail { get; set; }
    public string? Description { get; set; }
    public required long Size { get; set; }
    public required string MimeType { get; set; }

    public required int Width { get; set; }
    public required int Height { get; set; }
    
    [MaxLength(32)]
    public required string UploadId { get; set; }
    [MaxLength(32)]
    public required string PreviewUploadId { get; set; }

    public ChatContentType Type => ChatContentType.Image;
}