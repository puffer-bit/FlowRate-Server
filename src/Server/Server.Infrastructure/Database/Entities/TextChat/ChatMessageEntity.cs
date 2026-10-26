using System.ComponentModel.DataAnnotations;
using Server.Core.Models.User;

namespace Server.Infrastructure.Database.Entities.TextChat;

public class ChatMessageEntity
{
    [MaxLength(36)]
    public int Id { get; set; }

    [MaxLength(36)]
    public required int ChatId { get; set; }

    public long Revision { get; set; }

    public required int SenderUserId { get; set; }

    public DateTime SentTime { get; set; }
    public DateTime? DelayedSentTime { get; set; }
    public DateTime? DisposeTime { get; set; }
    public DateTime? EditTime { get; set; }

    public ChatTextContentEntity? TextContent { get; set; }
    public int? ReplyToMessageId { get; set; }

    /// <summary>
    /// Arbitrary media items (image, video, file). Stored as separate rows.
    /// </summary>
    // separate tables for each media type so we keep structure matching
    // the application models (image, video, file).
    public List<ChatImageContentEntity> Images { get; set; } = new();
    public List<ChatVideoContentEntity> Videos { get; set; } = new();
    public List<ChatFileContentEntity> Files { get; set; } = new();

    /// <summary>
    /// Reactions applied to this message.  Each row represents a single
    /// reaction (emoji + optional user) to allow counting and auditing.
    /// </summary>
    public List<ChatReactionEntity> Reactions { get; set; } = new();
    public List<string> UsersIds { get; set; } = new();

    public bool IsDelayed { get; set; }
    public bool IsEdited { get; set; }
    public bool IsReposted { get; set; }
    public bool IsDelivered { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsSystem { get; set; }
    public bool IsDisposable { get; set; }
    
    // Navigation fields
    public ChatSessionEntity ChatSession { get; set; } = null!;
}