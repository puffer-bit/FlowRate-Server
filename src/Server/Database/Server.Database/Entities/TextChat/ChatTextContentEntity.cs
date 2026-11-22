using Microsoft.EntityFrameworkCore;

namespace Server.Database.Entities.TextChat;

[Owned]
public class ChatTextContentEntity
{
    public required string Text { get; init; }

    public int? CommandKind { get; init; }
    public int? CommandMinimum { get; init; }
    public int? CommandMaximum { get; init; }
    public int? CommandValue { get; init; }

}