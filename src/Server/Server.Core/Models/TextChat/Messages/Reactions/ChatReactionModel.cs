namespace Server.Core.Models.TextChat.Messages.Reactions;

public sealed record ChatReactionModel
{
    public required int UserId { get; init; }
    public required string Emoji { get; init; }
}
