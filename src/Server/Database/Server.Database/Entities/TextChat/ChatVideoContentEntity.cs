using System.ComponentModel.DataAnnotations;

namespace Server.Database.Entities.TextChat;

public class ChatVideoContentEntity
{
    [MaxLength(36)]
    public required int Id { get; set; }
    
    [MaxLength(36)]
    public required int MessageId { get; set; }

    public required string FileName { get; set; }
    public string? Thumbnail { get; set; }
    public string? Description { get; set; }
    public required long Size { get; set; }
    public required string MimeType { get; set; }

    public required int Width { get; set; }
    public required int Height { get; set; }
    public required TimeSpan Duration { get; set; }
    
    
    public ICollection<ChatMessageEntity> Messages { get; set; }
}