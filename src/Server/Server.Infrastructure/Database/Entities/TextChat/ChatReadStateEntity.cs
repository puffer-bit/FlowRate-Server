namespace Server.Infrastructure.Database.Entities.TextChat;

public class ChatReadStateEntity
{
    public int UserId { get; set; }
    public int ChatId { get; set; }
    public int LastReadMessageId { get; set; }

    public ChatSessionEntity ChatSession { get; set; } = null!;
}
