using Server.Core.Models.TextChat.Messages.Content;
using Server.Core.Models.TextChat.Messages.Reactions;
using Server.Core.Models.User;
using Shared.Enums;

namespace Server.Core.Models.TextChat.Messages;

/// <summary>
/// A chat message. Facts fixed at sending are init-only; what may change afterwards
/// (text, reply, reactions, delivery) changes only through methods.
/// </summary>
public sealed class ChatMessageModel
{
    private ChatTextContentModel? _textContent;
    private ChatReplyContentModel? _replyContent;
    private DateTime? _editTime;
    private bool _isDelivered;
    private long _revision;
    private readonly List<ChatReactionModel> _reactions = new();

    // Assigned by the database after the first save
    public int Id { get; set; }
    public long Revision { get => _revision; init => _revision = value; }
    public required int ChatId { get; init; }
    public required int SenderUserId { get; init; }
    public required DateTime SentTime { get; init; }
    public DateTime? DelayedSentTime { get; init; }
    public DateTime? DisposeTime { get; init; }

    public DateTime? EditTime { get => _editTime; init => _editTime = value; }
    public ChatTextContentModel? TextContent { get => _textContent; init => _textContent = value; }
    public ChatReplyContentModel? ReplyContent { get => _replyContent; init => _replyContent = value; }

    public IReadOnlyList<ChatImageContentModel> Images { get; init; } = [];
    public IReadOnlyList<ChatVideoContentModel> Videos { get; init; } = [];
    public IReadOnlyList<ChatFileContentModel> Files { get; init; } = [];

    public IReadOnlyList<ChatReactionModel> Reactions
    {
        get => _reactions;
        init => _reactions = value.ToList();
    }

    public IReadOnlyList<string> UsersIds { get; init; } = [];

    public bool IsDelivered { get => _isDelivered; init => _isDelivered = value; }
    public bool IsReposted { get; init; }
    public bool IsDeleted { get; init; }
    public bool IsReadOnly { get; init; }
    public bool IsSystem { get; init; }

    public bool IsDelayed => DelayedSentTime.HasValue;
    public bool IsDisposable => DisposeTime.HasValue;
    public bool IsEdited => _editTime.HasValue;

    public static ChatMessageModel Create(int chatId, int senderUserId, DateTime now,
        ChatTextContentModel? text,
        IEnumerable<ChatImageContentModel>? images = null,
        IEnumerable<ChatVideoContentModel>? videos = null,
        IEnumerable<ChatFileContentModel>? files = null,
        DateTime? delayedSentTime = null,
        DateTime? disposeTime = null)
    {
        var message = new ChatMessageModel
        {
            ChatId = chatId,
            SenderUserId = senderUserId,
            SentTime = now,
            TextContent = text,
            Images = images?.ToList() ?? [],
            Videos = videos?.ToList() ?? [],
            Files = files?.ToList() ?? [],
            DelayedSentTime = delayedSentTime,
            DisposeTime = disposeTime
        };

        if (message.TextContent == null && !message.HasAttachments)
            throw new ArgumentException("A message needs text or an attachment.");

        return message;
    }

    public bool HasAttachments => Images.Count + Videos.Count + Files.Count > 0;

    public bool IsSentBy(int userId) => SenderUserId == userId;

    public void ReplyTo(ChatMessageModel target)
    {
        if (target.ChatId != ChatId)
            throw new InvalidOperationException("A reply must target a message of the same chat.");

        _replyContent = target.ToReplyContent();
    }

    public ErrorCode? Edit(int userId, string text, DateTime now)
    {
        if (!IsSentBy(userId) || IsDeleted || IsReadOnly || IsSystem)
            return ErrorCode.InsufficientPermissions;

        _textContent = new ChatTextContentModel { Text = text };
        _editTime = now;
        return null;
    }

    public void MarkDelivered() => _isDelivered = true;

    public void MarkChanged(long revision) => _revision = revision;

    /// <returns>False if the user already reacted with this emoji.</returns>
    public bool AddReaction(int userId, string emoji)
    {
        if (_reactions.Any(r => r.UserId == userId && r.Emoji == emoji))
            return false;

        _reactions.Add(new ChatReactionModel { UserId = userId, Emoji = emoji });
        return true;
    }

    public bool RemoveReaction(int userId, string emoji) =>
        _reactions.RemoveAll(r => r.UserId == userId && r.Emoji == emoji) > 0;

    public ChatReplyContentModel ToReplyContent() => new()
    {
        MessageId = Id,
        SenderUserId = SenderUserId,
        SentTime = SentTime,
        TextContent = TextContent,
        IsDeleted = IsDeleted
    };
}
