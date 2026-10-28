using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Tests.Fakes;
using Server.Core.Models.TextChat.Messages;
using Server.Core.Models.TextChat.Messages.Content;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Models.Requests.SessionActions.Generic.Models;
using Shared.Models.Requests.SessionActions.Generic.Models.SessionData;
using Server.Application.Abstractions.Persistence;
using Server.Application.Abstractions.Media;

namespace Server.Application.Tests.Coordinator;

public class ChatSessionManagerTests
{
    private const int AuthorId = 1;

    private readonly Mock<IChatSessionRepository> _chats = new();
    private readonly Mock<IMessagesRepository> _messages = new();
    private readonly RecordingEventBus _events = new();
    private readonly Dictionary<int, ChatMessageModel> _storedMessages = new();
    private readonly ChatSessionManager _manager;
    private int _nextChatId = 100;

    public ChatSessionManagerTests()
    {
        _chats.Setup(r => r.AddAsync(It.IsAny<Core.Models.TextChat.ChatSessionModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _nextChatId++);
        _messages.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => _storedMessages.GetValueOrDefault(id));

        _manager = new ChatSessionManager(NullLogger.Instance, _events, _chats.Object, _messages.Object,
            new Mock<IMediaUploadRepository>().Object, new Mock<IMediaStorage>().Object, "coordinator");
    }

    private static CreateSessionModel Chat(string name) => new(name, 10, new ChatSessionCreateData());

    private async Task<int> CreateChat(string name)
    {
        var (result, chat) = await _manager.CreateAsync(Chat(name), AuthorId);
        Assert.Equal(null, result);
        return chat!.Id;
    }

    private void StoreMessage(int messageId, int chatId)
    {
        var message = ChatMessageModel.Create(chatId, AuthorId, DateTime.UtcNow, new ChatTextContentModel { Text = "hello" });
        message.Id = messageId;
        _storedMessages[messageId] = message;
    }

    [Fact]
    public async Task Create_RegistersTheChatAndAnnouncesIt()
    {
        var chatId = await CreateChat("general");

        Assert.True(_manager.TryGetSession(chatId, out var chat));
        Assert.Equal("general", chat.Name);
        Assert.Single(_events.Of<ChatSessionCreatedEvent>());
    }

    [Fact]
    public async Task Create_SameNameTwiceAtOnce_OnlyOneSucceeds()
    {
        var results = await Task.WhenAll(
            _manager.CreateAsync(Chat("general"), AuthorId),
            _manager.CreateAsync(Chat("general"), AuthorId));

        Assert.Single(results, r => r.result == null);
        Assert.Single(results, r => r.result == ErrorCode.NameTaken);
        Assert.Single(_manager.GetAllSessions());
    }

    [Fact]
    public async Task Delete_RemovesTheChat()
    {
        var chatId = await CreateChat("general");

        Assert.Equal(null, await _manager.DeleteAsync(chatId, AuthorId));

        Assert.False(_manager.TryGetSession(chatId, out _));
        _chats.Verify(r => r.RemoveAsync(chatId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_UnknownChat_ReturnsSessionNotExists()
    {
        Assert.Equal(ErrorCode.SessionNotFound, await _manager.DeleteAsync(42, AuthorId));
    }

    [Fact]
    public async Task FetchMessage_OfAnotherChat_IsNotFound()
    {
        var chatId = await CreateChat("general");
        var otherChatId = await CreateChat("random");
        StoreMessage(messageId: 5, chatId: otherChatId);

        var (result, message) = await _manager.FetchMessageAsync(chatId, 5);

        Assert.Equal(ErrorCode.MessageNotFound, result);
        Assert.Null(message);
    }

    [Fact]
    public async Task FetchMessage_OfAChatOfAnotherCoordinator_IsNotFound()
    {
        StoreMessage(messageId: 5, chatId: 42);

        var (result, message) = await _manager.FetchMessageAsync(42, 5);

        Assert.Equal(ErrorCode.MessageNotFound, result);
        Assert.Null(message);
    }

    [Fact]
    public async Task FetchMessages_OfAChatOfAnotherCoordinator_IsRefusedWithoutReadingThem()
    {
        var (result, messages) = await _manager.FetchMessagesAsync(42, 10, null);

        Assert.Equal(ErrorCode.ChatNotFound, result);
        Assert.Null(messages);
        _messages.Verify(r => r.GetByTakeSkipAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FetchMessage_OfTheSameChat_IsFound()
    {
        var chatId = await CreateChat("general");
        StoreMessage(messageId: 5, chatId: chatId);

        var (result, message) = await _manager.FetchMessageAsync(chatId, 5);

        Assert.Equal(null, result);
        Assert.Equal(5, message!.Id);
    }

    [Fact]
    public async Task DeleteMessage_OfAnotherChat_IsNotDeleted()
    {
        var chatId = await CreateChat("general");
        var otherChatId = await CreateChat("random");
        StoreMessage(messageId: 5, chatId: otherChatId);

        var result = await _manager.DeleteMessageAsync(chatId, 5, AuthorId);

        Assert.Equal(ErrorCode.MessageNotFound, result);
        _messages.Verify(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EditMessage_OfAnotherChat_IsNotEdited()
    {
        var chatId = await CreateChat("general");
        var otherChatId = await CreateChat("random");
        StoreMessage(messageId: 5, chatId: otherChatId);

        var result = await _manager.EditMessageAsync(chatId, 5, "changed", AuthorId);

        Assert.Equal(ErrorCode.MessageNotFound, result);
        _messages.Verify(r => r.UpdateAsync(It.IsAny<ChatMessageModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteMessage_AnnouncesTheRevisionOfTheDeletion()
    {
        var chatId = await CreateChat("general");
        StoreMessage(messageId: 5, chatId: chatId);
        _messages.Setup(r => r.DeleteAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(7);

        Assert.Equal(null, await _manager.DeleteMessageAsync(chatId, 5, AuthorId));

        var deleted = Assert.Single(_events.Of<ChatSessionMessageDeletedEvent>());
        Assert.Equal((5, 7L), (deleted.MessageId, deleted.Revision));
    }

    [Fact]
    public async Task DeleteMessage_AlreadyDeleted_IsNotAnnouncedAgain()
    {
        var chatId = await CreateChat("general");
        StoreMessage(messageId: 5, chatId: chatId);
        _messages.Setup(r => r.DeleteAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync((long?)null);

        Assert.Equal(ErrorCode.MessageNotFound, await _manager.DeleteMessageAsync(chatId, 5, AuthorId));
        Assert.Empty(_events.Of<ChatSessionMessageDeletedEvent>());
    }

    [Fact]
    public async Task EditMessage_AnnouncesTheNewRevision()
    {
        var chatId = await CreateChat("general");
        StoreMessage(messageId: 5, chatId: chatId);
        _messages.Setup(r => r.UpdateAsync(It.IsAny<ChatMessageModel>(), It.IsAny<CancellationToken>())).ReturnsAsync(9);

        Assert.Equal(null, await _manager.EditMessageAsync(chatId, 5, "changed", AuthorId));

        Assert.Equal(9, Assert.Single(_events.Of<ChatSessionMessageEditedEvent>()).Message.Revision);
    }

    [Fact]
    public async Task FetchMessagesBefore_ReturnsThePageWithTheChatRevision()
    {
        var chatId = await CreateChat("general");
        _messages.Setup(r => r.GetRevisionAsync(chatId, It.IsAny<CancellationToken>())).ReturnsAsync(12);
        _messages.Setup(r => r.GetBeforeAsync(chatId, 40, 30, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var (result, page) = await _manager.FetchMessagesBeforeAsync(chatId, 40, 30);

        Assert.Equal(null, result);
        Assert.Equal(12, page!.Revision);
    }

    [Fact]
    public async Task FetchMessagesBefore_UnknownChat_ReturnsChatNotExists()
    {
        var (result, page) = await _manager.FetchMessagesBeforeAsync(42, null, 30);

        Assert.Equal(ErrorCode.ChatNotFound, result);
        Assert.Null(page);
    }

    [Fact]
    public async Task FetchChanges_PartOfThem_ReportsHasMore()
    {
        var chatId = await CreateChat("general");
        StoreMessage(messageId: 5, chatId: chatId);
        _messages.Setup(r => r.GetRevisionAsync(chatId, It.IsAny<CancellationToken>())).ReturnsAsync(12);
        _messages.Setup(r => r.CountChangesAsync(chatId, 3, It.IsAny<CancellationToken>())).ReturnsAsync(4);
        _messages.Setup(r => r.GetChangesAsync(chatId, 3, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_storedMessages[5]]);

        var (result, changes) = await _manager.FetchChangesAsync(chatId, 3, 1);

        Assert.Equal(null, result);
        Assert.Single(changes!.Messages);
        Assert.True(changes.HasMore);
        Assert.False(changes.Reset);
        Assert.Equal(12, changes.Revision);
    }

    [Fact]
    public async Task FetchChanges_TooMany_AsksToReset()
    {
        var chatId = await CreateChat("general");
        _messages.Setup(r => r.GetRevisionAsync(chatId, It.IsAny<CancellationToken>())).ReturnsAsync(900);
        _messages.Setup(r => r.CountChangesAsync(chatId, 3, It.IsAny<CancellationToken>())).ReturnsAsync(501);

        var (_, changes) = await _manager.FetchChangesAsync(chatId, 3, 100);

        Assert.True(changes!.Reset);
        Assert.Empty(changes.Messages);
        _messages.Verify(r => r.GetChangesAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FetchChanges_FromARevisionTheChatNeverHad_AsksToReset()
    {
        var chatId = await CreateChat("general");
        _messages.Setup(r => r.GetRevisionAsync(chatId, It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var (_, changes) = await _manager.FetchChangesAsync(chatId, 50, 100);

        Assert.True(changes!.Reset);
        Assert.Equal(2, changes.Revision);
    }
}
