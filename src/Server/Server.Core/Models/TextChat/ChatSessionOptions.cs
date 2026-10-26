namespace Server.Core.Models.TextChat;

/// <summary>What members of a chat session are allowed to post.</summary>
public sealed record ChatSessionOptions(
    bool IsReadOnly = false,
    bool IsAttachmentsAllowed = false,
    bool IsVoiceMessagesAllowed = false,
    bool IsVideoMessagesAllowed = false,
    bool IsDelayedMessagesAllowed = false)
{
    public static ChatSessionOptions Default { get; } = new();
}
