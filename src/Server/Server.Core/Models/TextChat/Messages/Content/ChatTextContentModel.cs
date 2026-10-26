namespace Server.Core.Models.TextChat.Messages.Content;

public sealed record ChatTextContentModel
{
    public required string Text { get; init; }
}
