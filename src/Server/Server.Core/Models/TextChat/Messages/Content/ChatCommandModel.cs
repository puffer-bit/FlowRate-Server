using Shared.Models.DataTransferObjects.ChatSession.Messages.Commands;

namespace Server.Core.Models.TextChat.Messages.Content;

public sealed record ChatCommandModel(ChatCommandKind Kind, int Minimum, int Maximum)
{
    public bool IsValid => Kind switch
    {
        ChatCommandKind.Roll => Minimum >= 1 && Minimum <= Maximum && Maximum <= 1000,
        ChatCommandKind.Flip => Minimum == 0 && Maximum == 1,
        _ => false
    };
}
