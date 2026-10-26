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
        _repository.Setup(r => r.MoveAsync(UserId, ChatId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, int _, int messageId, CancellationToken _) => messageId);
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

        Assert.Equal(ChatReadResult.NoError, result);
        Assert.Equal(30, state!.LastReadMessageId);
        _sender.Verify(s => s.SendMessageByUserId(UserId,
            It.Is<ChatReadStateChangedNotification>(n => n.State.ChatId == ChatId && n.State.UnreadCount == 0)),
            Times.Once);
    }

    [Fact]
    public async Task MarkRead_OfAChatThatDoesNotExist_ChangesNothing()
    {
        var (result, _) = await _service.MarkReadAsync(UserId, ChatId + 1, 3);

        Assert.Equal(ChatReadResult.ChatNotExists, result);
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OwnMessage_MovesThePositionOfItsSender()
    {
        var message = ChatMessageModel.Create(ChatId, UserId, DateTime.UtcNow, new ChatTextContentModel { Text = "hi" });
        message.Id = 31;

        await _service.HandleAsync(new ChatSessionMessageAddedEvent(ChatId, message));

        _repository.Verify(r => r.MoveAsync(UserId, ChatId, 31, It.IsAny<CancellationToken>()), Times.Once);
    }
}
