namespace Server.Core.Models.TextChat.Messages.Reactions;

public sealed record ChatReactionModel
{
    public required int UserId { get; init; }
    public required string Emoji { get; init; }

    /// <summary>Null for reactions set before the time was kept.</summary>
    public DateTime? ReactedAt { get; init; }
}
