using Server.Core.Models.TextChat.Messages;
using Server.Core.Models.TextChat.Messages.Content;

namespace Server.Core.Tests.Models;

public class ChatMessageLinkPreviewTests
{
    private const int AuthorId = 1;
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ChatMessageModel Message(string text, bool disabled = false) =>
        ChatMessageModel.Create(7, AuthorId, Now, new ChatTextContentModel { Text = text }, isLinkPreviewDisabled: disabled);

    private static ChatLinkPreviewModel Preview(string url, int id = 1) => new()
    {
        Id = id, CoordinatorInstanceId = "c", Url = url, Title = "T", FetchedAt = Now
    };

    [Fact]
    public void TheTarget_IsTheFirstAddress_WithoutItsFragment()
    {
        var message = Message("look www.Example.com/a#top and https://b.example");

        Assert.Equal("https://www.example.com/a", message.LinkPreviewTarget);
        Assert.True(message.NeedsLinkPreview);
    }

    [Fact]
    public void AMessageWithThePreviewTurnedOff_HasNoTarget()
    {
        var message = Message("https://example.com", disabled: true);

        Assert.Null(message.LinkPreviewTarget);
        Assert.False(message.NeedsLinkPreview);
        Assert.False(message.SetLinkPreview(Preview("https://example.com/")));
    }

    [Fact]
    public void APreviewOfAnotherAddress_IsNotTaken()
    {
        var message = Message("https://example.com/a");

        Assert.False(message.SetLinkPreview(Preview("https://example.com/b")));
        Assert.True(message.SetLinkPreview(Preview("https://example.com/a")));
        Assert.False(message.NeedsLinkPreview);
        Assert.False(message.SetLinkPreview(Preview("https://example.com/a")));
    }

    [Fact]
    public void EditingTheAddress_DropsTheOldPreview()
    {
        var message = Message("https://example.com/a");
        message.SetLinkPreview(Preview("https://example.com/a"));

        message.Edit(AuthorId, "now https://example.com/b", Now);

        Assert.Null(message.LinkPreview);
        Assert.True(message.NeedsLinkPreview);
    }

    [Fact]
    public void EditingAroundTheAddress_KeepsThePreview()
    {
        var message = Message("https://example.com/a");
        message.SetLinkPreview(Preview("https://example.com/a"));

        message.Edit(AuthorId, "see https://example.com/a", Now);

        Assert.NotNull(message.LinkPreview);
    }

    [Fact]
    public void AnEdit_TurnsThePreviewOffAndOn_OrLeavesIt()
    {
        var message = Message("https://example.com/a");
        message.SetLinkPreview(Preview("https://example.com/a"));

        message.Edit(AuthorId, "https://example.com/a", Now, isLinkPreviewDisabled: true);
        Assert.True(message.IsLinkPreviewDisabled);
        Assert.Null(message.LinkPreview);

        message.Edit(AuthorId, "https://example.com/a!", Now);
        Assert.True(message.IsLinkPreviewDisabled);

        message.Edit(AuthorId, "https://example.com/a", Now, isLinkPreviewDisabled: false);
        Assert.True(message.NeedsLinkPreview);
    }
}
