using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Core.Models.TextChat.Messages;
using Server.Core.Models.TextChat.Messages.Content;
using Server.Infrastructure.Database;
using Server.Infrastructure.Database.Entities.TextChat;
using Server.Infrastructure.Database.Repositories;
using Xunit;

namespace Server.Infrastructure.Tests.Database;

public sealed class MessagesRepositoryTests : IDisposable
{
    private const int AuthorId = 1;

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"messages-{Guid.NewGuid():N}.db");
    private readonly TestContextFactory _factory;
    private readonly MessagesRepository _repository;
    private readonly int _chatId;

    public MessagesRepositoryTests()
    {
        _factory = new TestContextFactory(new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options);
        _repository = new MessagesRepository(_factory);

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

    private ChatMessageModel NewMessage(string text) => ChatMessageModel.Create(
        _chatId,
        AuthorId,
        DateTime.UtcNow,
        new ChatTextContentModel { Text = text });

    private async Task<int> Add(string text, int? replyTo = null)
    {
        var message = NewMessage(text);
        if (replyTo is { } targetId)
            message.ReplyTo((await _repository.GetByIdAsync(targetId))!);

        return (await _repository.AddAsync(message)).Id;
    }

    private async Task Edit(int messageId, string text)
    {
        var message = (await _repository.GetByIdAsync(messageId))!;
        message.Edit(AuthorId, text, DateTime.UtcNow);
        await _repository.UpdateAsync(message);
    }

    [Fact]
    public async Task EveryChange_TakesTheNextRevisionOfTheChat()
    {
        var first = await _repository.AddAsync(NewMessage("one"));
        var (firstId, firstRevision) = (first.Id, first.Revision);
        var secondRevision = (await _repository.AddAsync(NewMessage("two"))).Revision;

        var edited = (await _repository.GetByIdAsync(firstId))!;
        edited.Edit(AuthorId, "one, fixed", DateTime.UtcNow);
        var editRevision = await _repository.UpdateAsync(edited);
        var deleteRevision = await _repository.DeleteAsync(firstId);

        Assert.Equal([1, 2, 3, 4], new[] { firstRevision, secondRevision, editRevision, deleteRevision!.Value });
        Assert.Equal(4, await _repository.GetRevisionAsync(_chatId));
        Assert.Equal(4, (await _repository.GetByIdAsync(firstId))!.Revision);
    }

    [Fact]
    public async Task Add_ReturnsTheMessageAsItIsReadBack()
    {
        var targetId = await Add("question");
        var reply = NewMessage("answer");
        reply.ReplyTo((await _repository.GetByIdAsync(targetId))!);

        var added = await _repository.AddAsync(reply);
        var read = (await _repository.GetByIdAsync(added.Id))!;

        Assert.NotEqual(0, added.Id);
        Assert.Equal(read.Revision, added.Revision);
        Assert.Equal(read.ChatId, added.ChatId);
        Assert.Equal(read.SenderUserId, added.SenderUserId);
        Assert.Equal(read.SentTime, added.SentTime);
        Assert.Equal(read.TextContent, added.TextContent);
        Assert.Equal(read.ReplyContent, added.ReplyContent);
    }

    [Fact]
    public async Task ParallelWrites_GetDistinctRevisionsWithoutGaps()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(i => Task.Run(() => _repository.AddAsync(NewMessage($"message {i}")))));

        Assert.Equal(Enumerable.Range(1, 20).Select(i => (long)i), results.Select(r => r.Revision).Order());
        Assert.Equal(20, await _repository.GetRevisionAsync(_chatId));
    }

    [Fact]
    public async Task DeletingTwice_ChangesNothingTheSecondTime()
    {
        var id = await Add("one");

        Assert.Equal(2, await _repository.DeleteAsync(id));
        Assert.Null(await _repository.DeleteAsync(id));
        Assert.Equal(2, await _repository.GetRevisionAsync(_chatId));
    }

    [Fact]
    public async Task Changes_ReturnCurrentStateOnceAndDeletedAsTombstones()
    {
        var kept = await Add("kept");
        var edited = await Add("draft");
        var deleted = await Add("secret");
        await Edit(edited, "second draft");
        await Edit(edited, "final");
        await _repository.DeleteAsync(deleted);

        var changes = await _repository.GetChangesAsync(_chatId, 1, 100);

        Assert.Equal([edited, deleted], changes.Select(m => m.Id));
        Assert.Equal([5, 6], changes.Select(m => m.Revision));
        Assert.Equal("final", changes[0].TextContent!.Text);
        Assert.True(changes[1].IsDeleted);
        Assert.Null(changes[1].TextContent);
        Assert.Equal(2, await _repository.CountChangesAsync(_chatId, 1));
        Assert.DoesNotContain(changes, m => m.Id == kept);
    }

    [Fact]
    public async Task Changes_AreCutByTakeInRevisionOrder()
    {
        for (var i = 0; i < 5; i++)
            await Add($"message {i}");

        var changes = await _repository.GetChangesAsync(_chatId, 0, 2);

        Assert.Equal([1, 2], changes.Select(m => m.Revision));
        Assert.Equal(5, await _repository.CountChangesAsync(_chatId, 0));
    }

    [Fact]
    public async Task Before_PagesByIdAndSkipsDeleted()
    {
        var ids = new List<int>();
        for (var i = 0; i < 6; i++)
            ids.Add(await Add($"message {i}"));
        await _repository.DeleteAsync(ids[4]);

        var newest = await _repository.GetBeforeAsync(_chatId, null, 3);
        var older = await _repository.GetBeforeAsync(_chatId, newest[0].Id, 3);

        Assert.Equal([ids[2], ids[3], ids[5]], newest.Select(m => m.Id));
        Assert.Equal([ids[0], ids[1]], older.Select(m => m.Id));
    }

    [Fact]
    public async Task ReplyPreview_FollowsTheTarget()
    {
        var target = await Add("original");
        var reply = await Add("answer", replyTo: target);

        Assert.Equal("original", (await _repository.GetByIdAsync(reply))!.ReplyContent!.TextContent!.Text);

        await Edit(target, "changed");
        var afterEdit = (await _repository.GetBeforeAsync(_chatId, null, 10)).Single(m => m.Id == reply);
        Assert.Equal(target, afterEdit.ReplyContent!.MessageId);
        Assert.Equal("changed", afterEdit.ReplyContent.TextContent!.Text);
        Assert.Equal(AuthorId, afterEdit.ReplyContent.SenderUserId);

        await _repository.DeleteAsync(target);
        var afterDelete = (await _repository.GetByIdAsync(reply))!;
        Assert.True(afterDelete.ReplyContent!.IsDeleted);
        Assert.Null(afterDelete.ReplyContent.TextContent);
        Assert.Equal(2, afterDelete.Revision);
    }

    private sealed class TestContextFactory(DbContextOptions<ServerDbContext> options) : IDbContextFactory<ServerDbContext>
    {
        public ServerDbContext CreateDbContext() => new(options);
    }
}
