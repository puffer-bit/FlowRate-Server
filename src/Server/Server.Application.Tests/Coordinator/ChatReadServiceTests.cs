using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Core.Models.TextChat;
using Server.Core.Models.TextChat.Messages;
using Server.Core.Models.TextChat.Messages.Content;
using Shared.Enums;
using Shared.Models.Notifications.SessionInfo;

namespace Server.Application.Tests.Coordinator;

public class ChatReadServiceTests
{
    private const string CoordinatorId = "coordinator";
    private const int UserId = 5;
    private const int ChatId = 7;

    private readonly Mock<IChatReadStateRepository> _repository = new();
    private readonly Mock<IChatSessionManager> _chats = new();
    private readonly Mock<IMessageSender> _sender = new();
    private readonly ChatReadService _service;

    public ChatReadServiceTests()
    {
        var chat = ChatSessionModel.Create(CoordinatorId, "general", 1);
        chat.Id = ChatId;
        _chats.Setup(c => c.TryGetSession(ChatId, out chat)).Returns(true);
        _chats.Setup(c => c.GetAllSessions()).Returns([chat]);
        _repository.Setup(r => r.MoveAsync(UserId, ChatId, It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, int _, int messageId, DateTime? _, CancellationToken _) => new ChatReadMove(messageId, true));
        _repository.Setup(r => r.GetLastMessageIdAsync(ChatId, It.IsAny<CancellationToken>())).ReturnsAsync(30);

        _service = new ChatReadService(NullLogger.Instance, _repository.Object, _sender.Object);
        _service.Watch(_chats.Object);
    }

    private void Unread(params ChatUnreadMessage[] messages) =>
        _repository.Setup(r => r.GetUnreadAsync(ChatId, UserId, It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(messages);

    [Fact]
    public async Task States_CountMentionsByIdAndEveryone_ButNotHere()
    {
        _repository.Setup(r => r.GetPositionsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, int> { [ChatId] = 10 });
        Unread(new(11, "hi <@5>"), new(12, "<@everyone> lunch"), new(13, "<@here>"), new(14, "<@55>"), new(15, null));

        var state = Assert.Single(await _service.GetStatesAsync(UserId));

        Assert.Equal(new ChatReadState(ChatId, 10, 5, 2), state);
    }

    [Fact]
    public async Task States_OfAChatNeverOpened_CountFromItsStart()
    {
        _repository.Setup(r => r.GetPositionsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, int>());
        Unread(new(1, "first"), new(2, "second"));

        var state = Assert.Single(await _service.GetStatesAsync(UserId));

        Assert.Equal(new ChatReadState(ChatId, 0, 2, 0), state);
        _repository.Verify(r => r.GetUnreadAsync(ChatId, UserId, 0, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MarkRead_StopsAtTheLastMessage_AndTellsTheDevicesOfTheUser()
    {
        Unread();

        var (result, state) = await _service.MarkReadAsync(UserId, ChatId, 99);

        Assert.Equal(null, result);
        Assert.Equal(30, state!.LastReadMessageId);
        _sender.Verify(s => s.SendMessageByUserId(UserId,
            It.Is<ChatReadStateChangedNotification>(n => n.State.ChatId == ChatId && n.State.UnreadCount == 0)),
            Times.Once);
    }

    [Fact]
    public async Task MarkRead_OfAChatThatDoesNotExist_ChangesNothing()
    {
        var (result, _) = await _service.MarkReadAsync(UserId, ChatId + 1, 3);

        Assert.Equal(ErrorCode.ChatNotFound, result);
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OwnMessage_MovesThePositionOfItsSender()
    {
        var message = ChatMessageModel.Create(ChatId, UserId, DateTime.UtcNow, new ChatTextContentModel { Text = "hi" });
        message.Id = 31;

        await _service.HandleAsync(new ChatSessionMessageAddedEvent(ChatId, message));

        _repository.Verify(r => r.MoveAsync(UserId, ChatId, 31, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private const int GroupId = 8;
    private const int FriendId = 6;
    private const int StrangerId = 9;
    private static readonly DateTime Now = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    private (ChatReadService service, ChatSessionModel group) WithGroup(int peerPosition = 0, bool moved = true)
    {
        var group = ChatSessionModel.Create(CoordinatorId, "friends", UserId);
        group.Id = GroupId;
        group.SetAudience(ChatKind.Group, [UserId, FriendId, StrangerId]);
        _chats.Setup(c => c.TryGetSession(GroupId, out group)).Returns(true);
        _repository.Setup(r => r.GetLastMessageIdAsync(GroupId, It.IsAny<CancellationToken>())).ReturnsAsync(30);
        _repository.Setup(r => r.MoveAsync(UserId, GroupId, It.IsAny<int>(), It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, int _, int messageId, DateTime? _, CancellationToken _) => new ChatReadMove(messageId, moved));
        _repository.Setup(r => r.GetUnreadAsync(GroupId, UserId, It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _repository.Setup(r => r.GetPeerPositionsAsync(UserId, It.IsAny<IReadOnlyCollection<int>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, IReadOnlyCollection<int> ids, CancellationToken _) =>
                ids.Contains(GroupId) ? new Dictionary<int, int> { [GroupId] = peerPosition } : new Dictionary<int, int>());

        var service = new ChatReadService(NullLogger.Instance, _repository.Object, _sender.Object,
            new FixedTime(Now));
        service.Watch(_chats.Object);
        return (service, group);
    }

    [Fact]
    public async Task MarkRead_InAGroup_KeepsTheTime_AndTellsTheOtherMembers()
    {
        var (service, _) = WithGroup(peerPosition: 12);

        var (_, state) = await service.MarkReadAsync(UserId, GroupId, 20);

        Assert.Equal(12, state!.PeerReadMessageId);
        _repository.Verify(r => r.MoveAsync(UserId, GroupId, 20, Now, It.IsAny<CancellationToken>()), Times.Once);
        _sender.Verify(s => s.BroadcastMessage(
            It.Is<IEnumerable<int>>(ids => ids.OrderBy(id => id).SequenceEqual(new[] { FriendId, StrangerId })),
            It.Is<ChatPeerReadNotification>(n => n.ChatId == GroupId && n.ReaderUserId == UserId
                                                 && n.LastReadMessageId == 20 && n.ReadAt == Now)), Times.Once);
    }

    [Fact]
    public async Task MarkRead_WithoutProgress_TellsNobodyElse()
    {
        var (service, _) = WithGroup(moved: false);

        await service.MarkReadAsync(UserId, GroupId, 20);

        _sender.Verify(s => s.BroadcastMessage(It.IsAny<IEnumerable<int>>(), It.IsAny<ChatPeerReadNotification>()),
            Times.Never);
    }

    [Fact]
    public async Task MarkRead_InAChannel_HasNoReceipts()
    {
        Unread();

        var (_, state) = await _service.MarkReadAsync(UserId, ChatId, 10);

        Assert.Equal(0, state!.PeerReadMessageId);
        _repository.Verify(r => r.MoveAsync(UserId, ChatId, 10, null, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.GetPeerPositionsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _sender.Verify(s => s.BroadcastMessage(It.IsAny<IEnumerable<int>>(), It.IsAny<ChatPeerReadNotification>()),
            Times.Never);
    }

    [Fact]
    public async Task OwnMessage_InAGroup_TellsTheOthersTheyAreRead()
    {
        var (service, _) = WithGroup();
        var message = ChatMessageModel.Create(GroupId, UserId, Now, new ChatTextContentModel { Text = "hi" });
        message.Id = 25;

        await service.HandleAsync(new ChatSessionMessageAddedEvent(GroupId, message));

        _sender.Verify(s => s.BroadcastMessage(It.IsAny<IEnumerable<int>>(),
            It.Is<ChatPeerReadNotification>(n => n.LastReadMessageId == 25)), Times.Once);
    }

    [Fact]
    public async Task States_CarryThePeerPosition_OnlyOfPrivateChatsOfTheUser()
    {
        var (service, group) = WithGroup(peerPosition: 14);
        var foreign = ChatSessionModel.Create(CoordinatorId, "others", FriendId);
        foreign.Id = 77;
        foreign.SetAudience(ChatKind.Group, [FriendId, StrangerId]);
        _chats.Setup(c => c.GetAllSessions()).Returns([group, foreign]);
        _repository.Setup(r => r.GetPositionsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, int>());
        _repository.Setup(r => r.GetUnreadAsync(It.IsAny<int>(), UserId, It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var states = await service.GetStatesAsync(UserId);

        Assert.Equal(14, states.Single(state => state.ChatId == GroupId).PeerReadMessageId);
        _repository.Verify(r => r.GetPeerPositionsAsync(UserId,
            It.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { GroupId })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Readers_AreForTheAuthorOnly()
    {
        var (service, _) = WithGroup();
        _repository.Setup(r => r.GetTargetAsync(GroupId, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatReadTarget(FriendId, false, false));
        _repository.Setup(r => r.GetTargetAsync(GroupId, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatReadTarget(UserId, false, false));
        _repository.Setup(r => r.GetReadersAsync(GroupId, 4, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChatMessageReader(FriendId, Now)]);

        Assert.Equal(ErrorCode.NotMessageAuthor, (await service.GetReadersAsync(UserId, GroupId, 3)).result);
        Assert.Equal(ErrorCode.MessageNotFound, (await service.GetReadersAsync(UserId, GroupId, 5)).result);
        var (result, readers) = await service.GetReadersAsync(UserId, GroupId, 4);
        Assert.Null(result);
        Assert.Equal([new ChatMessageReader(FriendId, Now)], readers);
    }

    [Fact]
    public async Task Readers_OfAChannel_AreNotKept()
    {
        var (result, _) = await _service.GetReadersAsync(UserId, ChatId, 3);

        Assert.Equal(ErrorCode.WrongSessionType, result);
        _repository.Verify(r => r.GetTargetAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
