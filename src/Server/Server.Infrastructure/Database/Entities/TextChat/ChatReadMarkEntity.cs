namespace Server.Infrastructure.Database.Entities.TextChat;

/// <summary>
/// One forward move of a read position in a direct chat or a group: the user read up to
/// <see cref="UpToMessageId"/> at <see cref="ReadAt"/>. A message was read by a user at the earliest mark
/// that reaches it.
/// </summary>
public class ChatReadMarkEntity
{
    public int ChatId { get; set; }
    public int UserId { get; set; }
    public int UpToMessageId { get; set; }
    public DateTime ReadAt { get; set; }

    public ChatSessionEntity ChatSession { get; set; } = null!;
}
