using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Infrastructure.Database.Entities.TextChat;

[Table("ChatSessions")]
public class ChatSessionEntity : SessionEntity
{
    public int CreatorUserId { get; set; }
    public int AdminUserId { get; set; }
    
    public long LastRevision { get; set; }

    public string? Description { get; set; }
    public string? IconPath { get; set; }

    public bool IsReadOnly { get; set; }
    public bool IsArchived { get; set; }
    public bool IsAttachmentsAllowed { get; set; }
    public bool IsVoiceMessagesAllowed { get; set; }
    public bool IsVideoMessagesAllowed { get; set; }
    public bool IsDelayedMessagesAllowed { get; set; }

    // Navigation fields
    public ICollection<ChatMessageEntity> Messages { get; set; } = null!;
}