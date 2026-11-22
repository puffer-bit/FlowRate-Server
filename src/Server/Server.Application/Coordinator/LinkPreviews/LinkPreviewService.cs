using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Server.Application.Abstractions.LinkPreviews;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Media;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Core.Models.TextChat.Messages;
using Server.Core.Models.TextChat.Messages.Content;

namespace Server.Application.Coordinator.LinkPreviews;

/// <summary>
/// Makes the previews of links in messages: fetches pages, keeps what they said for a while, and puts a
/// preview into a message once its page is known. Every message that should show a preview and has none
/// gets one when it is sent or changed; the composer asks for the same previews before sending.
/// </summary>
public sealed class LinkPreviewService : IEventHandler<ChatSessionMessageAddedEvent>,
    IEventHandler<ChatSessionMessageEditedEvent>, IDisposable
{
    /// <summary>A page fetched within this time is not fetched again.</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(1);

    /// <summary>A page that could not be fetched, or had nothing to show, is not tried again within this time.</summary>
    public static readonly TimeSpan FailureLifetime = TimeSpan.FromMinutes(10);

    // Older than the cache with a margin, so that a preview just taken from it is not removed under a message
    private static readonly TimeSpan UnusedLifetime = CacheLifetime * 2;
    private static readonly TimeSpan CleanUpInterval = TimeSpan.FromHours(6);

    // Pages a user may have fetched in a row, and how fast that comes back; cached pages do not count
    private const int UserFetchBurst = 20;
    private static readonly TimeSpan UserFetchRefill = TimeSpan.FromSeconds(10);

    private const int MaxConcurrentFetches = 4;
    private const int MaxRememberedFailures = 10_000;

    private readonly ILogger _logger;
    private readonly ILinkPreviewRepository _repository;
    private readonly ILinkPreviewFetcher _fetcher;
    private readonly ILinkPreviewImageStore _images;
    private readonly TimeProvider _time;
    private readonly string _coordinatorInstanceId;

    private readonly SemaphoreSlim _fetchSlots = new(MaxConcurrentFetches, MaxConcurrentFetches);
    private readonly ConcurrentDictionary<string, Task<ChatLinkPreviewModel?>> _inFlight = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _failures = new();
    private readonly ConcurrentDictionary<int, UserBudget> _budgets = new();
    // Messages waiting for their preview, by message id and address, so that one is not fetched twice
    private readonly ConcurrentDictionary<(int MessageId, string Url), byte> _pending = new();
    private readonly CancellationTokenSource _stopping = new();

    private IChatSessionManager? _chats;
    private Task? _cleanUpLoop;

    public LinkPreviewService(ILogger logger, ILinkPreviewRepository repository, ILinkPreviewFetcher fetcher,
        ILinkPreviewImageStore images, string coordinatorInstanceId, TimeProvider? time = null)
    {
        _logger = logger;
        _repository = repository;
        _fetcher = fetcher;
        _images = images;
        _coordinatorInstanceId = coordinatorInstanceId;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Starts putting previews into the messages of these chats.</summary>
    public void Watch(IChatSessionManager chats) => _chats = chats;

    public void Start() => _cleanUpLoop ??= Task.Run(CleanUpLoopAsync);

    /// <summary>
    /// The preview of an address, from the cache or fetched now. A fetch counts against the user, so that
    /// nobody makes the server fetch pages without end.
    /// </summary>
    /// <returns>Null when the page has nothing to show, cannot be fetched, or the user fetched too many.</returns>
    public async Task<ChatLinkPreviewModel?> GetAsync(Uri url, int userId, CancellationToken cancellationToken = default)
    {
        if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
            return null;

        var key = LinkPreviewAddress.KeyOf(url);
        var now = _time.GetUtcNow();

        if (_failures.TryGetValue(key, out var failedAt))
        {
            if (now - failedAt < FailureLifetime)
                return null;
            _failures.TryRemove(key, out _);
        }

        var cached = await _repository.GetNewestAsync(_coordinatorInstanceId, key, now - CacheLifetime, cancellationToken);
        if (cached != null)
            return cached;

        // Someone fetches it already: the same answer is good for both, and costs nothing more
        if (_inFlight.TryGetValue(key, out var running))
            return await running.WaitAsync(cancellationToken);

        if (!TryTakeBudget(userId, now))
            return null;

        // Registered before the fetch starts, and removed before its answer is given, so that a finished
        // fetch never stays behind and a later one after the cache has run out goes ahead
        var source = new TaskCompletionSource<ChatLinkPreviewModel?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetch = _inFlight.GetOrAdd(key, source.Task);
        if (fetch == source.Task)
            _ = Task.Run(async () =>
            {
                var preview = await FetchAndStoreAsync(key);
                _inFlight.TryRemove(KeyValuePair.Create(key, source.Task));
                source.SetResult(preview);
            });
        return await fetch.WaitAsync(cancellationToken);
    }

    public async Task<MediaFile?> OpenImageAsync(int previewId, CancellationToken cancellationToken = default)
    {
        var preview = await _repository.GetAsync(_coordinatorInstanceId, previewId, cancellationToken);
        if (preview is not { ImageMimeType: { } mimeType })
            return null;

        var content = await _images.OpenReadAsync(previewId, cancellationToken);
        return content == null ? null : new MediaFile(content, mimeType);
    }

    public Task HandleAsync(ChatSessionMessageAddedEvent evt, CancellationToken ct = default)
    {
        Schedule(evt.ChatId, evt.Message);
        return Task.CompletedTask;
    }

    public Task HandleAsync(ChatSessionMessageEditedEvent evt, CancellationToken ct = default)
    {
        Schedule(evt.ChatId, evt.Message);
        return Task.CompletedTask;
    }

    // Events come while the chat is locked for writing, so the fetch goes on its own
    private void Schedule(int chatId, ChatMessageModel message)
    {
        if (_chats == null || !message.NeedsLinkPreview || message.LinkPreviewTarget is not { } target)
            return;
        if (!_pending.TryAdd((message.Id, target), 0))
            return;

        var messageId = message.Id;
        var senderId = message.SenderUserId;
        _ = Task.Run(async () =>
        {
            try
            {
                var preview = await GetAsync(new Uri(target), senderId, _stopping.Token);
                if (preview != null)
                    await _chats.SetLinkPreviewAsync(chatId, messageId, preview);
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Making the link preview of message {MessageId} failed.", messageId);
            }
            finally
            {
                _pending.TryRemove((messageId, target), out _);
            }
        });
    }

    private async Task<ChatLinkPreviewModel?> FetchAndStoreAsync(string key)
    {
        try
        {
            await _fetchSlots.WaitAsync(_stopping.Token);
            FetchedLinkPreview? fetched;
            try
            {
                fetched = await _fetcher.FetchAsync(new Uri(key), _stopping.Token);
            }
            finally
            {
                _fetchSlots.Release();
            }

            if (fetched == null)
            {
                RememberFailure(key);
                return null;
            }

            var preview = await _repository.AddAsync(new ChatLinkPreviewModel
            {
                CoordinatorInstanceId = _coordinatorInstanceId,
                Url = key,
                SiteName = fetched.SiteName,
                Title = fetched.Title,
                Description = fetched.Description,
                ImageMimeType = fetched.Image?.MimeType,
                ImageWidth = fetched.Image?.Width ?? 0,
                ImageHeight = fetched.Image?.Height ?? 0,
                ImageSize = fetched.Image?.Content.LongLength ?? 0,
                IsLargeImage = fetched.Image != null && fetched.IsLargeImage,
                FetchedAt = _time.GetUtcNow()
            }, _stopping.Token);

            if (fetched.Image != null)
                await _images.SaveAsync(preview.Id, fetched.Image.Content, _stopping.Token);

            return preview;
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Storing the link preview of a page failed.");
            RememberFailure(key);
            return null;
        }
    }

    private void RememberFailure(string key)
    {
        // Many different failing addresses should not grow the memory without end
        if (_failures.Count >= MaxRememberedFailures)
            _failures.Clear();
        _failures[key] = _time.GetUtcNow();
    }

    private bool TryTakeBudget(int userId, DateTimeOffset now)
    {
        var budget = _budgets.GetOrAdd(userId, _ => new UserBudget(UserFetchBurst, now));
        lock (budget)
        {
            var refilled = (now - budget.UpdatedAt) / UserFetchRefill;
            budget.Tokens = Math.Min(UserFetchBurst, budget.Tokens + refilled);
            budget.UpdatedAt = now;
            if (budget.Tokens < 1)
                return false;
            budget.Tokens--;
            return true;
        }
    }

    public async Task CleanUpAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var removed = await _repository.RemoveUnusedAsync(_coordinatorInstanceId, now - UnusedLifetime, cancellationToken);
        foreach (var id in removed)
        {
            try
            {
                await _images.DeleteAsync(id, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Deleting the image of link preview {PreviewId} failed.", id);
            }
        }

        foreach (var (key, failedAt) in _failures)
            if (now - failedAt >= FailureLifetime)
                _failures.TryRemove(key, out _);

        foreach (var (userId, budget) in _budgets)
            if (now - budget.UpdatedAt > UserFetchRefill * UserFetchBurst)
                _budgets.TryRemove(userId, out _);

        if (removed.Count > 0)
            _logger.LogInformation("Removed {Count} link previews that no message shows.", removed.Count);
    }

    private async Task CleanUpLoopAsync()
    {
        var token = _stopping.Token;
        try
        {
            using var timer = new PeriodicTimer(CleanUpInterval, _time);
            do
            {
                try
                {
                    await CleanUpAsync(token);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(exception, "Cleaning up link previews failed.");
                }
            } while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException)
        {
        }
    }

    // The source is not disposed: fetches still running read its token while they end
    public void Dispose()
    {
        _stopping.Cancel();
        (_fetcher as IDisposable)?.Dispose();
    }

    private sealed class UserBudget(double tokens, DateTimeOffset updatedAt)
    {
        public double Tokens { get; set; } = tokens;
        public DateTimeOffset UpdatedAt { get; set; } = updatedAt;
    }
}
