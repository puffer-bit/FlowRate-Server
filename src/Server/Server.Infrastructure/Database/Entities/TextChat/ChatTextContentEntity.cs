using Microsoft.EntityFrameworkCore;
using Shared.Models.DataTransferObjects.ChatSession.Messages;

namespace Server.Infrastructure.Database.Entities.TextChat;

[Owned]
public class ChatTextContentEntity
{
    public required string Text { get; init; }

    public int? CommandKind { get; init; }
    public int? CommandMinimum { get; init; }
    public int? CommandMaximum { get; init; }
    public int? CommandValue { get; init; }

    public ChatContentType Type => ChatContentType.Text;
}