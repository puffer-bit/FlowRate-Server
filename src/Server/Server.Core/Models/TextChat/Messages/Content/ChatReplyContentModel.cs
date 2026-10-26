using Server.Core.Models.User;

namespace Server.Core.Models.TextChat.Messages.Content;

/// <summary>The replied message as it is now. Only its id is stored with the reply.</summary>
public sealed record ChatReplyContentModel
{
    public required int MessageId { get; init; }
    public int? SenderUserId { get; init; }
    public DateTime SentTime { get; init; }
    public ChatTextContentModel? TextContent { get; init; }
    public bool IsDeleted { get; init; }
}
