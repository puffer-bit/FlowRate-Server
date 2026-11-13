using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Core.Models.TextChat;
using Server.Core.Models.TextChat.Messages;
using Server.Core.Models.TextChat.Messages.Content;
using Server.Infrastructure.Database;
using Server.Infrastructure.Database.Entities.TextChat;
using Server.Infrastructure.Database.Repositories;
using Xunit;

namespace Server.Infrastructure.Tests.Database;

public sealed class ChatReadStateRepositoryTests : IDisposable
{
    private const int ReaderId = 1;
    private const int OtherId = 2;

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"reads-{Guid.NewGuid():N}.db");
    private readonly TestContextFactory _factory;
    private readonly ChatReadStateRepository _repository;
    private readonly MessagesRepository _messages;
    private readonly int _chatId;

    public ChatReadStateRepositoryTests()
    {
        _factory = new TestContextFactory(new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options);
        _repository = new ChatReadStateRepository(_factory);
        _messages = new MessagesRepository(_factory);

        using var context = _factory.CreateDbContext();
        context.Database.Migrate();

        var chat = new ChatSessionEntity { Id = 0, Name = "general", CoordinatorInstanceId = "coordinator" };
        context.ChatSessionEntities.Add(chat);
        context.SaveChanges();
        _chatId = chat.Id;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }

    private async Task<int> Add(int senderId, string text) =>
        (await _messages.AddAsync(ChatMessageModel.Create(_chatId, senderId, DateTime.UtcNow,
            new ChatTextContentModel { Text = text }))).Id;

    [Fact]
    public async Task Move_NeverGoesBack()
    {
        Assert.Equal(new ChatReadMove(10, true), await _repository.MoveAsync(ReaderId, _chatId, 10, null));
        Assert.Equal(new ChatReadMove(10, false), await _repository.MoveAsync(ReaderId, _chatId, 4, null));
        Assert.Equal(new ChatReadMove(12, true), await _repository.MoveAsync(ReaderId, _chatId, 12, null));

        Assert.Equal(12, (await _repository.GetPositionsAsync(ReaderId))[_chatId]);
        Assert.Empty(await _repository.GetPositionsAsync(OtherId));
    }

    [Fact]
    public async Task Unread_AreTheMessagesOfOthersAfterThePosition()
    {
        var read = await Add(OtherId, "seen");
        await Add(ReaderId, "mine");
        var deleted = await Add(OtherId, "gone");
        await _messages.DeleteAsync(deleted);
        var first = await Add(OtherId, "new <@1>");
        var second = await Add(OtherId, "newer");

        var unread = await _repository.GetUnreadAsync(_chatId, ReaderId, read, 100);

        Assert.Equal([first, second], unread.Select(message => message.Id));
        Assert.Equal("new <@1>", unread[0].Text);
        Assert.Single(await _repository.GetUnreadAsync(_chatId, ReaderId, read, 1));
        Assert.Equal(second, await _repository.GetLastMessageIdAsync(_chatId));
    }

    [Fact]
    public async Task Positions_GoAwayWithTheirChat()
    {
        await _repository.MoveAsync(ReaderId, _chatId, 3, DateTime.UtcNow);

        await new ChatSessionRepository(_factory).RemoveAsync(_chatId);

        Assert.Empty(await _repository.GetPositionsAsync(ReaderId));
    }

    private const int ThirdId = 3;
    private static readonly DateTime Noon = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Readers_GetTheTimeOfTheFirstMarkThatReachedTheMessage()
    {
        var first = await Add(ReaderId, "one");
        var second = await Add(ReaderId, "two");
        await _repository.MoveAsync(OtherId, _chatId, first, Noon);
        await _repository.MoveAsync(OtherId, _chatId, second, Noon.AddMinutes(5));
        await _repository.MoveAsync(ThirdId, _chatId, second, Noon.AddMinutes(1));
        await _repository.MoveAsync(ReaderId, _chatId, second, Noon);

        var ofFirst = await _repository.GetReadersAsync(_chatId, first, ReaderId);
        var ofSecond = await _repository.GetReadersAsync(_chatId, second, ReaderId);

        Assert.Equal([new ChatMessageReader(OtherId, Noon), new ChatMessageReader(ThirdId, Noon.AddMinutes(1))],
            ofFirst);
        Assert.Equal([new ChatMessageReader(ThirdId, Noon.AddMinutes(1)),
            new ChatMessageReader(OtherId, Noon.AddMinutes(5))], ofSecond);
        Assert.All(ofFirst, reader => Assert.Equal(DateTimeKind.Utc, reader.ReadAt!.Value.Kind));
    }

    [Fact]
    public async Task Readers_OfAPositionWithoutMarks_HaveNoTime()
    {
        var message = await Add(ReaderId, "old");
        await _repository.MoveAsync(OtherId, _chatId, message, null);

        Assert.Equal([new ChatMessageReader(OtherId, null)],
            await _repository.GetReadersAsync(_chatId, message, ReaderId));
    }

    [Fact]
    public async Task Move_WithoutProgress_KeepsNoMark()
    {
        var message = await Add(ReaderId, "hi");
        await _repository.MoveAsync(OtherId, _chatId, message, Noon);
        var again = await _repository.MoveAsync(OtherId, _chatId, message, Noon.AddHours(1));

        Assert.False(again.Moved);
        Assert.Equal(Noon, Assert.Single(await _repository.GetReadersAsync(_chatId, message, ReaderId)).ReadAt);
    }

    [Fact]
    public async Task PeerPositions_AreTheFurthestOfTheOthers()
    {
        await _repository.MoveAsync(ReaderId, _chatId, 40, null);
        await _repository.MoveAsync(OtherId, _chatId, 7, null);
        await _repository.MoveAsync(ThirdId, _chatId, 9, null);

        Assert.Equal(9, (await _repository.GetPeerPositionsAsync(ReaderId, [_chatId]))[_chatId]);
        Assert.Equal(40, (await _repository.GetPeerPositionsAsync(OtherId, [_chatId]))[_chatId]);
        Assert.Empty(await _repository.GetPeerPositionsAsync(ReaderId, []));
    }

    [Fact]
    public async Task Target_TellsTheSenderOfTheMessage()
    {
        var message = await Add(OtherId, "hi");

        Assert.Equal(new ChatReadTarget(OtherId, false, false), await _repository.GetTargetAsync(_chatId, message));
        Assert.Null(await _repository.GetTargetAsync(_chatId, message + 100));
    }

    private sealed class TestContextFactory(DbContextOptions<ServerDbContext> options) : IDbContextFactory<ServerDbContext>
    {
        public ServerDbContext CreateDbContext() => new(options);
    }
}
