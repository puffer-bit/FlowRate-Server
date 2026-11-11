using Shared.Models.DataTransferObjects.ChatSession.Messages.Commands;

namespace Server.Core.Models.TextChat.Messages.Content;

/// <summary>The server's stored outcome. Clients never choose the result.</summary>
public sealed record ChatCommandResultModel
{
    public required ChatCommandKind Kind { get; init; }
    public required int Minimum { get; init; }
    public required int Maximum { get; init; }
    public required int Value { get; init; }

    public bool IsValid() => Kind switch
    {
        ChatCommandKind.Roll => Minimum >= 1 && Minimum <= Maximum && Maximum <= 1000
            && Value >= Minimum && Value <= Maximum,
        ChatCommandKind.Flip => Minimum == 0 && Maximum == 1 && Value is 0 or 1,
        _ => false
    };

    public string ToFallbackText() => Kind == ChatCommandKind.Roll
        ? FormattableString.Invariant($"/roll {Minimum}-{Maximum} → {Value}")
        : "/flip → " + (Value == 0 ? "heads" : "tails");
}

