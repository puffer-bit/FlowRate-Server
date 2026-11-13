namespace Server.Infrastructure.Database.Entities.TextChat;

/// <summary>Persistent chat membership, retaining the original user id after account deletion.</summary>
public class ChatSessionMemberEntity
{
    public int ChatSessionId { get; set; }
    public int UserId { get; set; }
    public DateTime JoinedAt { get; set; }
    public int? AccountId { get; set; }

    public ChatSessionEntity ChatSession { get; set; } = null!;
}
