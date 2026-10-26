using System.ComponentModel.DataAnnotations;

namespace Server.Infrastructure.Database.Entities.TextChat;

public class ChatReactionEntity
{
    [MaxLength(36)]
    public required int UserId { get; set; }

    public required string Emoji { get; init; }
}