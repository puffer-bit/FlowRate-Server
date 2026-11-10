using Shared.Models.DataTransferObjects.ChatSession.Messages.Commands;

namespace Server.Core.Models.TextChat.Messages.Content;

public sealed record ChatTextContentModel
{
    public required string Text { get; init; }
    public ChatCommandResult? CommandResult { get; init; }
}
