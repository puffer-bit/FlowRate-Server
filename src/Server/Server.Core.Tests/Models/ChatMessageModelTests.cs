using Server.Core.Models.TextChat;
using Server.Core.Models.TextChat.Messages;
using Server.Core.Models.TextChat.Messages.Content;
using Server.Core.Models.User;
using Shared.Enums;

namespace Server.Core.Tests.Models;

public class ChatMessageModelTests
{
    private const int ChatId = 7;
    private const int AuthorId = 1;
    private const int OtherUserId = 2;
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ChatMessageModel Message(int chatId = ChatId) =>
        ChatMessageModel.Create(chatId, AuthorId, Now, new ChatTextContentModel { Text = "hello" });

    [Fact]
    public void Create_WithoutTextAndAttachments_Throws()
    {
        Assert.Throws<ArgumentException>(() => ChatMessageModel.Create(ChatId, AuthorId, Now, text: null));
    }

    [Fact]
    public void Create_DerivesDelayAndDisposalFromTimes()
    {
        var message = ChatMessageModel.Create(ChatId, AuthorId, Now, new ChatTextContentModel { Text = "x" },
            delayedSentTime: Now.AddHours(1));

        Assert.True(message.IsDelayed);
        Assert.False(message.IsDisposable);
        Assert.False(message.IsEdited);
    }

    [Fact]
    public void Edit_ByAuthor_ChangesTextAndEditTime()
    {
        var message = Message();
        var editTime = Now.AddMinutes(5);

        Assert.Equal(null, message.Edit(AuthorId, "changed", editTime));

        Assert.Equal("changed", message.TextContent!.Text);
        Assert.Equal(editTime, message.EditTime);
        Assert.True(message.IsEdited);
    }

    [Fact]
    public void Edit_ByOtherUser_IsRejected()
    {
        var message = Message();

        Assert.Equal(ErrorCode.InsufficientPermissions, message.Edit(OtherUserId, "changed", Now));
        Assert.Equal("hello", message.TextContent!.Text);
        Assert.False(message.IsEdited);
    }

    [Fact]
    public void ReplyTo_CopiesTargetAsItIsNow()
    {
        var target = Message();
        target.Id = 42;
        var reply = Message();

        reply.ReplyTo(target);
        target.Edit(AuthorId, "changed later", Now);

        Assert.Equal(42, reply.ReplyContent!.MessageId);
        Assert.Equal("hello", reply.ReplyContent.TextContent!.Text);
    }

    [Fact]
    public void ReplyTo_MessageOfOtherChat_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Message().ReplyTo(Message(chatId: ChatId + 1)));
    }

    [Fact]
    public void AddReaction_IgnoresDuplicates()
    {
        var message = Message();

        Assert.True(message.AddReaction(OtherUserId, "👍"));
        Assert.False(message.AddReaction(OtherUserId, "👍"));
        Assert.Single(message.Reactions);

        Assert.True(message.RemoveReaction(OtherUserId, "👍"));
        Assert.Empty(message.Reactions);
    }

    [Fact]
    public void Message_CanBeDeletedByAuthorOrChatAdmin()
    {
        const int adminId = 3;
        var chat = ChatSessionModel.Create("coordinator", "chat", creatorUserId: adminId);
        var message = Message();

        Assert.True(chat.CanDeleteMessage(message, AuthorId));
        Assert.True(chat.CanDeleteMessage(message, adminId));
        Assert.False(chat.CanDeleteMessage(message, OtherUserId));
    }
}
