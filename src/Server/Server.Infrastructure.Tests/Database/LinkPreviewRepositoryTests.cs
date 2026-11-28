using Microsoft.EntityFrameworkCore;
using Server.Core.Models.TextChat.Messages;
using Server.Core.Models.TextChat.Messages.Content;
using Server.Database;
using Server.Database.Entities.TextChat;
using Server.Infrastructure.Database.Repositories;
using Xunit;

namespace Server.Infrastructure.Tests.Database;

[Trait("Category", "DatabaseProvider")]
public sealed class LinkPreviewRepositoryTests : IDisposable
{
    private const string Coordinator = "coordinator";
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly RepositoryTestDatabase _database = new();
    private readonly LinkPreviewRepository _previews;
    private readonly MessagesRepository _messages;
    private readonly int _chatId;

    public LinkPreviewRepositoryTests()
    {
        var factory = new TestContextFactory(_database.Options);
        _previews = new LinkPreviewRepository(factory);
        _messages = new MessagesRepository(factory);

        using var context = factory.CreateDbContext();
        context.Database.Migrate();
        var chat = new ChatSessionEntity { Id = 0, Name = "general", CoordinatorInstanceId = Coordinator };
        context.ChatSessionEntities.Add(chat);
        context.SaveChanges();
        _chatId = chat.Id;
    }

    private sealed class TestContextFactory(DbContextOptions<ServerDbContext> options) : IDbContextFactory<ServerDbContext>
    {
        public ServerDbContext CreateDbContext() => new(options);
    }

    public void Dispose()
    {
        _database.Dispose();
    }

    private Task<ChatLinkPreviewModel> AddPreview(string url, DateTimeOffset fetchedAt) => _previews.AddAsync(new ChatLinkPreviewModel
    {
        CoordinatorInstanceId = Coordinator, Url = url, Title = "Title", Description = "Text",
        ImageMimeType = "image/png", ImageWidth = 1200, ImageHeight = 630, ImageSize = 100, IsLargeImage = true,
        FetchedAt = fetchedAt
    });

    [Fact]
    public async Task TheNewestFreshPreview_IsFound()
    {
        await AddPreview("https://example.com/", Now.AddHours(-30));
        var fresh = await AddPreview("https://example.com/", Now.AddHours(-1));

        Assert.Equal(fresh.Id, (await _previews.GetNewestAsync(Coordinator, "https://example.com/", Now.AddDays(-1)))?.Id);
        Assert.Null(await _previews.GetNewestAsync(Coordinator, "https://example.com/", Now));
        Assert.Null(await _previews.GetNewestAsync("other", "https://example.com/", Now.AddDays(-1)));
        Assert.Null(await _previews.GetAsync("other", fresh.Id));
    }

    [Fact]
    public async Task AMessage_ComesBackWithItsPreview()
    {
        var preview = await AddPreview("https://example.com/", Now);
        var message = await _messages.AddAsync(ChatMessageModel.Create(_chatId, 1, Now,
            new ChatTextContentModel { Text = "https://example.com" }));

        message.SetLinkPreview(preview);
        await _messages.UpdateAsync(message);

        var stored = await _messages.GetByIdAsync(message.Id);
        Assert.Equal(preview.Id, stored?.LinkPreview?.Id);
        Assert.Equal("Title", stored?.LinkPreview?.Title);
        Assert.True(stored?.LinkPreview?.HasImage);
    }

    [Fact]
    public async Task OnlyOldPreviewsNoMessageShows_AreRemoved()
    {
        var shown = await AddPreview("https://a.example/", Now.AddDays(-5));
        var unused = await AddPreview("https://b.example/", Now.AddDays(-5));
        var recent = await AddPreview("https://c.example/", Now);
        var message = await _messages.AddAsync(ChatMessageModel.Create(_chatId, 1, Now,
            new ChatTextContentModel { Text = "https://a.example" }));
        message.SetLinkPreview(shown);
        await _messages.UpdateAsync(message);
        // A deleted message still holds its preview until it is gone for good
        await _messages.DeleteAsync(message.Id);

        var removed = await _previews.RemoveUnusedAsync(Coordinator, Now.AddDays(-2));

        Assert.Equal([unused.Id], removed);
        Assert.NotNull(await _previews.GetAsync(Coordinator, shown.Id));
        Assert.NotNull(await _previews.GetAsync(Coordinator, recent.Id));
    }
}
