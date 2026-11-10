using Shared.Models.DataTransferObjects.ChatSession.Messages.Commands;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Content;
using Shared.Models.Requests.SessionActions.ChatSession.Models;
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

    [Theory]
    [InlineData("/roll 7-7", ChatCommandKind.Roll)]
    [InlineData("/flip", ChatCommandKind.Flip)]
    public async Task Command_StoresAndPublishesOneServerGeneratedOutcome(string text, ChatCommandKind kind)
    {
        int chatId = await CreateChat("commands");
        ChatMessageModel? stored = null;
        _messages.Setup(r => r.AddAsync(It.IsAny<ChatMessageModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMessageModel message, CancellationToken _) =>
            {
                stored = message;
                message.Id = 5;
                _storedMessages[5] = message;
                return message;
            });
        var forged = new ChatCommandResult { Kind = ChatCommandKind.Roll, Minimum = 1, Maximum = 1000, Value = 999 };
        Assert.Null(await _manager.AddMessageAsync(chatId, new NewChatMessageModel
        {
            ChatId = chatId, TextContent = new ChatTextContentDTO { Text = text, CommandResult = forged }
        }, AuthorId));
        Assert.NotNull(stored);
        var outcome = stored.TextContent!.CommandResult!;
        Assert.True(outcome.IsValid());
        Assert.Equal(kind, outcome.Kind);
        Assert.NotEqual(forged, outcome);
        if (kind == ChatCommandKind.Roll) Assert.Equal(7, outcome.Value);
        var notification = Assert.Single(_events.Of<ChatSessionMessageAddedEvent>());
        Assert.Same(stored, notification.Message);
        _messages.Verify(r => r.AddAsync(It.IsAny<ChatMessageModel>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(ErrorCode.InsufficientPermissions, await _manager.EditMessageAsync(chatId, 5, "/roll 1-1000", AuthorId));
        Assert.Equal(outcome, stored.TextContent.CommandResult);
    }

    [Theory]
    [InlineData("/roll 100-1")]
    [InlineData("/roll 1-1001")]
    [InlineData("/flip heads")]
    public async Task InvalidCommand_DoesNotStoreOrPublishMessage(string text)
    {
        int chatId = await CreateChat("invalid-commands");
        Assert.Equal(ErrorCode.InvalidValue, await _manager.AddMessageAsync(chatId, new NewChatMessageModel
        {
            ChatId = chatId, TextContent = new ChatTextContentDTO { Text = text }
        }, AuthorId));
        _messages.Verify(r => r.AddAsync(It.IsAny<ChatMessageModel>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_events.Of<ChatSessionMessageAddedEvent>());
    }

    [Fact]
    public async Task Pins_AreIdempotent_AndDoNotEditText()
    {
        var chatId = await CreateChat("pins");
        StoreMessage(5, chatId);
        _messages.Setup(r => r.GetPinnedAsync(chatId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _messages.Setup(r => r.UpdateAsync(It.IsAny<ChatMessageModel>(), It.IsAny<CancellationToken>())).ReturnsAsync(9);
        Assert.Null(await _manager.SetPinnedAsync(chatId, 5, true, 2));
        var pinnedAt = _storedMessages[5].PinnedAt;
        Assert.NotNull(pinnedAt);
        Assert.Equal(2, _storedMessages[5].PinnedByUserId);
        Assert.Null(await _manager.SetPinnedAsync(chatId, 5, true, 3));
        Assert.Equal(pinnedAt, _storedMessages[5].PinnedAt);
        Assert.Equal(2, _storedMessages[5].PinnedByUserId);
        Assert.Null(_storedMessages[5].EditTime);
        Assert.Null(await _manager.SetPinnedAsync(chatId, 5, false, 3));
        Assert.Null(_storedMessages[5].PinnedAt);
        Assert.Null(_storedMessages[5].PinnedByUserId);
        Assert.Equal(2, _events.Of<ChatSessionMessageEditedEvent>().Count());
        _messages.Verify(r => r.UpdateAsync(It.IsAny<ChatMessageModel>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task PinLimitDoesNotPreventUnpinningOrIdempotentRequests()
    {
        var chat = await CreateChat("limit");
        StoreMessage(5, chat);
        _messages.Setup(r => r.GetPinnedAsync(chat, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Repeat(_storedMessages[5], 100).ToList());
        Assert.Equal(ErrorCode.InvalidRequest, await _manager.SetPinnedAsync(chat, 5, true, 2));
        _storedMessages[5].SetPinned(true, 1, DateTime.UtcNow);
        Assert.Null(await _manager.SetPinnedAsync(chat, 5, true, 2));
        Assert.Null(await _manager.SetPinnedAsync(chat, 5, false, 2));
        Assert.False(_storedMessages[5].IsPinned);
        _messages.Verify(r => r.UpdateAsync(It.IsAny<ChatMessageModel>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Pins_RejectCrossChatDeletedSystemAndArchivedMessages()
    {
        var chat = await CreateChat("pins");
        var other = await CreateChat("other");
        StoreMessage(5, other);
        Assert.Equal(ErrorCode.MessageNotFound, await _manager.SetPinnedAsync(chat, 5, true, 2));
        foreach (var system in new[] { true, false })
        {
            _storedMessages[5] = new ChatMessageModel
            {
                Id = 5, ChatId = chat, SenderUserId = 1, SentTime = DateTime.UtcNow,
                IsSystem = system, IsDeleted = !system
            };
            Assert.NotNull(await _manager.SetPinnedAsync(chat, 5, true, 2));
        }
        StoreMessage(5, chat);
        _manager.TryGetSession(chat, out var model);
        model!.Archive();
        Assert.Equal(ErrorCode.ChatReadOnly, await _manager.SetPinnedAsync(chat, 5, true, 2));
        _messages.Verify(r => r.UpdateAsync(It.IsAny<ChatMessageModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Window_CentersOnTarget_AndContinuesDownToLiveEnd()
    {
        var chat = await CreateChat("window");
        foreach (var id in new[] { 1, 3, 5, 8, 10, 11 }) StoreMessage(id, chat);
        _messages.Setup(r => r.GetBeforeAsync(chat, It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, int? before, int take, CancellationToken token) => _storedMessages.Values
                .Where(x => x.ChatId == chat && (!before.HasValue || x.Id < before))
                .OrderByDescending(x => x.Id).Take(take).OrderBy(x => x.Id).ToList());
        _messages.Setup(r => r.GetAfterAsync(chat, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, int after, int take, CancellationToken token) => _storedMessages.Values
                .Where(x => x.ChatId == chat && x.Id > after).OrderBy(x => x.Id).Take(take).ToList());
        _messages.Setup(r => r.GetRevisionAsync(chat, It.IsAny<CancellationToken>())).ReturnsAsync(50);
        var around = await _manager.FetchWindowAsync(chat, 5,
            Shared.Models.Requests.SessionActions.ChatSession.ChatHistoryDirection.Around, 3);
        Assert.Null(around.result);
        Assert.Equal(new[] { 3, 5, 8 }, around.window!.Messages.Select(x => x.Id));
        Assert.True(around.window.HasOlder);
        Assert.True(around.window.HasNewer);
        Assert.Equal(50, around.window.Revision);
        var after = await _manager.FetchWindowAsync(chat, 8,
            Shared.Models.Requests.SessionActions.ChatSession.ChatHistoryDirection.After, 3);
        Assert.Equal(new[] { 10, 11 }, after.window!.Messages.Select(x => x.Id));
        Assert.True(after.window.HasOlder);
        Assert.False(after.window.HasNewer);
    }

    [Fact]
    public async Task Window_RejectsMissingCrossChatAndDeletedTargets()
    {
        var chat = await CreateChat("window");
        var other = await CreateChat("other");
        StoreMessage(5, other);
        var direction = Shared.Models.Requests.SessionActions.ChatSession.ChatHistoryDirection.Around;
        Assert.Equal(ErrorCode.MessageNotFound, (await _manager.FetchWindowAsync(chat, 5, direction, 30)).result);
        Assert.Equal(ErrorCode.MessageNotFound, (await _manager.FetchWindowAsync(chat, 6, direction, 30)).result);
        _storedMessages[5] = new ChatMessageModel
        { Id = 5, ChatId = chat, SenderUserId = 1, SentTime = DateTime.UtcNow, IsDeleted = true };
        Assert.Equal(ErrorCode.MessageNotFound, (await _manager.FetchWindowAsync(chat, 5, direction, 30)).result);
    }

    [Fact]
    public async Task Reactions_ArePerUserAndIdempotent_WithoutEditingTheText()
    {
        var chatId = await CreateChat("general");
        StoreMessage(5, chatId);
        _messages.Setup(r => r.UpdateAsync(It.IsAny<ChatMessageModel>(), It.IsAny<CancellationToken>())).ReturnsAsync(9);

        Assert.Null(await _manager.SetReactionAsync(chatId, 5, "👍", true, 2));
        Assert.Null(await _manager.SetReactionAsync(chatId, 5, "👍", true, 2));
        Assert.Null(await _manager.SetReactionAsync(chatId, 5, "👍", true, 3));
        Assert.Equal(2, _storedMessages[5].Reactions.Count);
        Assert.Null(await _manager.SetReactionAsync(chatId, 5, "👍", false, 2));
        Assert.Null(await _manager.SetReactionAsync(chatId, 5, "👍", false, 2));

        var reaction = Assert.Single(_storedMessages[5].Reactions);
        Assert.Equal(3, reaction.UserId);
        Assert.Null(_storedMessages[5].EditTime);
        Assert.Equal("hello", _storedMessages[5].TextContent!.Text);
        Assert.Equal(3, _events.Of<ChatSessionMessageEditedEvent>().Count());
        Assert.All(_events.Of<ChatSessionMessageEditedEvent>(), e => Assert.Equal(9, e.Message.Revision));
        _messages.Verify(r => r.UpdateAsync(It.IsAny<ChatMessageModel>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Reactions_RejectUnknownChat_CrossChatMessage_AndInvalidEmoji()
    {
        var chatId = await CreateChat("general");
        var otherChat = await CreateChat("other");
        StoreMessage(5, otherChat);

        Assert.Equal(ErrorCode.ChatNotFound, await _manager.SetReactionAsync(999, 5, "👍", true, 2));
        Assert.Equal(ErrorCode.MessageNotFound, await _manager.SetReactionAsync(chatId, 5, "👍", true, 2));
        Assert.NotNull(await _manager.SetReactionAsync(otherChat, 5, "text", true, 2));
        _messages.Verify(r => r.UpdateAsync(It.IsAny<ChatMessageModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task Reactions_RejectDeletedSystemAndReadOnlyMessages(bool deleted, bool system, bool readOnly)
    {
        var chatId = await CreateChat("general");
        _storedMessages[5] = new ChatMessageModel
        {
            Id = 5, ChatId = chatId, SenderUserId = AuthorId, SentTime = DateTime.UtcNow,
            IsDeleted = deleted, IsSystem = system, IsReadOnly = readOnly
        };
        Assert.NotNull(await _manager.SetReactionAsync(chatId, 5, "👍", true, 2));
        _messages.Verify(r => r.UpdateAsync(It.IsAny<ChatMessageModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reactions_RejectArchivedChats()
    {
        var chatId = await CreateChat("general");
        StoreMessage(5, chatId);
        Assert.True(_manager.TryGetSession(chatId, out var chat));
        chat.Archive();
        Assert.Equal(ErrorCode.ChatReadOnly, await _manager.SetReactionAsync(chatId, 5, "👍", true, 2));
        _messages.Verify(r => r.UpdateAsync(It.IsAny<ChatMessageModel>(), It.IsAny<CancellationToken>()), Times.Never);
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
