using System.ComponentModel.DataAnnotations;

namespace Server.Database.Entities.TextChat;

public class ChatReactionEntity
{
    [MaxLength(36)]
    public required int UserId { get; set; }

    public required string Emoji { get; init; }

    public DateTimeOffset? ReactedAt { get; set; }
}