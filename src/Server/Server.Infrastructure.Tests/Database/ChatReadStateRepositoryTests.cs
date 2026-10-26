using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        Assert.Equal(10, await _repository.MoveAsync(ReaderId, _chatId, 10));
        Assert.Equal(10, await _repository.MoveAsync(ReaderId, _chatId, 4));
        Assert.Equal(12, await _repository.MoveAsync(ReaderId, _chatId, 12));

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
        await _repository.MoveAsync(ReaderId, _chatId, 3);

        await new ChatSessionRepository(_factory).RemoveAsync(_chatId);

        Assert.Empty(await _repository.GetPositionsAsync(ReaderId));
    }

    private sealed class TestContextFactory(DbContextOptions<ServerDbContext> options) : IDbContextFactory<ServerDbContext>
    {
        public ServerDbContext CreateDbContext() => new(options);
    }
}
