using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Server.Application.Abstractions.LinkPreviews;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.LinkPreviews;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Core.Models.TextChat.Messages;
using Server.Core.Models.TextChat.Messages.Content;
using Xunit;

namespace Server.Application.Tests.Coordinator;

public sealed class LinkPreviewServiceTests : IDisposable
{
    private const string Coordinator = "coordinator";

    private sealed class FakeFetcher : ILinkPreviewFetcher
    {
        public int Fetches;
        public FetchedLinkPreview? Answer = new("Site", "Title", "Text",
            new FetchedLinkImage([1, 2, 3], "image/png", 1200, 630), true);
        public TaskCompletionSource? Gate;

        public async Task<FetchedLinkPreview?> FetchAsync(Uri url, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Fetches);
            if (Gate != null)
                await Gate.Task.WaitAsync(cancellationToken);
            return Answer;
        }
    }

    private sealed class MemoryPreviews : ILinkPreviewRepository
    {
        public readonly List<ChatLinkPreviewModel> Rows = [];

        public Task<ChatLinkPreviewModel?> GetNewestAsync(string coordinatorInstanceId, string url, DateTime fetchedAfter,
            CancellationToken cancellationToken = default) => Task.FromResult(Rows
                .Where(x => x.CoordinatorInstanceId == coordinatorInstanceId && x.Url == url && x.FetchedAt >= fetchedAfter)
                .MaxBy(x => x.FetchedAt));

        public Task<ChatLinkPreviewModel?> GetAsync(string coordinatorInstanceId, int id,
            CancellationToken cancellationToken = default) => Task.FromResult(Rows.FirstOrDefault(x => x.Id == id));

        public Task<ChatLinkPreviewModel> AddAsync(ChatLinkPreviewModel preview, CancellationToken cancellationToken = default)
        {
            lock (Rows)
            {
                var stored = preview with { Id = Rows.Count + 1 };
                Rows.Add(stored);
                return Task.FromResult(stored);
            }
        }

        public Task<IReadOnlyList<int>> RemoveUnusedAsync(string coordinatorInstanceId, DateTime fetchedBefore,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<int>>([]);
    }

    private sealed class MemoryImages : ILinkPreviewImageStore
    {
        public readonly Dictionary<int, byte[]> Files = [];
        public Task SaveAsync(int previewId, byte[] content, CancellationToken cancellationToken = default)
        {
            lock (Files) Files[previewId] = content;
            return Task.CompletedTask;
        }
        public Task<Stream?> OpenReadAsync(int previewId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(previewId, out var content) ? new MemoryStream(content) : null);
        public Task DeleteAsync(int previewId, CancellationToken cancellationToken = default)
        {
            Files.Remove(previewId);
            return Task.CompletedTask;
        }
    }

    private readonly FakeFetcher _fetcher = new();
    private readonly MemoryPreviews _repository = new();
    private readonly MemoryImages _images = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly LinkPreviewService _service;

    public LinkPreviewServiceTests()
    {
        _service = new LinkPreviewService(NullLogger.Instance, _repository, _fetcher, _images, Coordinator, _time);
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task APage_IsFetchedOnce_AndKeptWithItsImage()
    {
        var first = await _service.GetAsync(new Uri("https://example.com/a#x"), 1);
        var second = await _service.GetAsync(new Uri("https://EXAMPLE.com/a"), 2);

        Assert.Equal(1, _fetcher.Fetches);
        Assert.Equal(first?.Id, second?.Id);
        Assert.Equal("https://example.com/a", first?.Url);
        Assert.True(first?.IsLargeImage);
        var image = await _service.OpenImageAsync(first!.Id);
        Assert.Equal("image/png", image?.MimeType);
    }

    [Fact]
    public async Task APageIsFetchedAgain_OnceTheCacheIsOld()
    {
        await _service.GetAsync(new Uri("https://example.com/"), 1);
        _time.Advance(LinkPreviewService.CacheLifetime + TimeSpan.FromMinutes(1));

        var again = await _service.GetAsync(new Uri("https://example.com/"), 1);

        Assert.Equal(2, _fetcher.Fetches);
        Assert.Equal(2, again?.Id);
    }

    [Fact]
    public async Task AFailure_IsNotTriedAgainForAWhile()
    {
        _fetcher.Answer = null;

        Assert.Null(await _service.GetAsync(new Uri("https://example.com/"), 1));
        Assert.Null(await _service.GetAsync(new Uri("https://example.com/"), 1));
        Assert.Equal(1, _fetcher.Fetches);

        _time.Advance(LinkPreviewService.FailureLifetime);
        await _service.GetAsync(new Uri("https://example.com/"), 1);
        Assert.Equal(2, _fetcher.Fetches);
    }

    [Fact]
    public async Task AUser_CannotMakeTheServerFetchWithoutEnd()
    {
        var answered = 0;
        for (var i = 0; i < 40; i++)
            if (await _service.GetAsync(new Uri($"https://example.com/{i}"), 1) != null)
                answered++;

        Assert.Equal(20, answered);
        // Another user is not held back, and cached pages are free
        Assert.NotNull(await _service.GetAsync(new Uri("https://example.com/39"), 2));
        Assert.NotNull(await _service.GetAsync(new Uri("https://example.com/0"), 1));
    }

    [Fact]
    public async Task OnePageAskedTwiceAtOnce_IsFetchedOnce()
    {
        _fetcher.Gate = new TaskCompletionSource();
        var first = _service.GetAsync(new Uri("https://example.com/"), 1);
        var second = _service.GetAsync(new Uri("https://example.com/"), 2);
        _fetcher.Gate.SetResult();

        Assert.Equal((await first)?.Id, (await second)?.Id);
        Assert.Equal(1, _fetcher.Fetches);
    }

    [Fact]
    public async Task ANewMessage_GetsThePreviewOfItsLink()
    {
        var chats = new Mock<IChatSessionManager>();
        var delivered = new TaskCompletionSource<ChatLinkPreviewModel>();
        chats.Setup(x => x.SetLinkPreviewAsync(7, 5, It.IsAny<ChatLinkPreviewModel>()))
            .Callback<int, int, ChatLinkPreviewModel>((_, _, preview) => delivered.TrySetResult(preview))
            .Returns(Task.CompletedTask);
        _service.Watch(chats.Object);

        var message = ChatMessageModel.Create(7, 1, DateTime.UtcNow, new ChatTextContentModel { Text = "see https://example.com/a" });
        message.Id = 5;
        await _service.HandleAsync(new ChatSessionMessageAddedEvent(7, message));

        var preview = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("https://example.com/a", preview.Url);
    }

    [Fact]
    public async Task AMessageWithoutALinkOrWithThePreviewOff_IsLeftAlone()
    {
        var chats = new Mock<IChatSessionManager>();
        _service.Watch(chats.Object);

        await _service.HandleAsync(new ChatSessionMessageAddedEvent(7,
            ChatMessageModel.Create(7, 1, DateTime.UtcNow, new ChatTextContentModel { Text = "no link" })));
        await _service.HandleAsync(new ChatSessionMessageAddedEvent(7,
            ChatMessageModel.Create(7, 1, DateTime.UtcNow, new ChatTextContentModel { Text = "https://example.com" },
                isLinkPreviewDisabled: true)));
        await Task.Delay(100);

        Assert.Equal(0, _fetcher.Fetches);
    }
}
