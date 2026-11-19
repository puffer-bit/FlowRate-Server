using System.ComponentModel.DataAnnotations;
using Shared.Models.DataTransferObjects.ChatSession.Messages;

namespace Server.Database.Entities.TextChat;

public class ChatFileContentEntity
{
    [MaxLength(36)]
    public required int Id { get; set; }
    
    [MaxLength(36)]
    public required int MessageId { get; set; }

    public required string FileName { get; set; }
    public required long Size { get; set; }
    public required string MimeType { get; set; }

    [MaxLength(32)]
    public required string UploadId { get; set; }

    // Set for a file that plays as a video
    public bool IsVideo { get; set; }
    public int VideoWidth { get; set; }
    public int VideoHeight { get; set; }
    public TimeSpan? VideoDuration { get; set; }

    [MaxLength(32)]
    public string? PreviewUploadId { get; set; }

    public ChatContentType Type => ChatContentType.File;
}