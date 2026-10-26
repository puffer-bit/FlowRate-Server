using System.Net.ServerSentEvents;
using Microsoft.Extensions.Hosting;
using Server.Asp.Web.Realtime;
using Server.Asp.Web.Realtime.Sse;

namespace Server.Asp.Tests.Web.Realtime;

public sealed class SsePanelNotifierTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private readonly FakeLifetime _lifetime = new();
    private readonly SsePanelNotifier _notifier;

    public SsePanelNotifierTests()
    {
        _notifier = new SsePanelNotifier(_lifetime);
    }

    public Task InitializeAsync() => _notifier.StartAsync(CancellationToken.None);

    public async Task DisposeAsync()
    {
        await _notifier.StopAsync(CancellationToken.None);
        _notifier.Dispose();
    }

    [Fact]
    public async Task Subscribe_StartsWithConnected()
    {
        await using var stream = _notifier.SubscribeAsync(CancellationToken.None).GetAsyncEnumerator();

        var first = await NextAsync(stream);

        Assert.Equal(PanelEventTypes.Connected, first.EventType);
        Assert.Equal(new PanelEvent(PanelEventTypes.Connected), first.Data);
    }

    [Fact]
    public async Task Notify_BurstForOneCoordinator_IsDeliveredOncePerCoordinator()
    {
        using var cancel = new CancellationTokenSource();
        await using var stream = _notifier.SubscribeAsync(cancel.Token).GetAsyncEnumerator();
        await NextAsync(stream);

        _notifier.NotifyCoordinatorChanged("a");
        _notifier.NotifyCoordinatorChanged("a");
        _notifier.NotifyCoordinatorChanged("b");
        _notifier.NotifyCoordinatorChanged("a");

        var changed = new[] { await NextAsync(stream), await NextAsync(stream) };

        Assert.All(changed, item => Assert.Equal(PanelEventTypes.CoordinatorChanged, item.EventType));
        Assert.Equal(["a", "b"], changed.Select(item => item.Data.CoordinatorId).Order());

        // Nothing else arrives before the subscription is cancelled
        cancel.CancelAfter(TimeSpan.FromMilliseconds(600));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.MoveNextAsync().AsTask());
    }

    [Fact]
    public async Task Notify_ReachesEverySubscriber()
    {
        await using var first = _notifier.SubscribeAsync(CancellationToken.None).GetAsyncEnumerator();
        await using var second = _notifier.SubscribeAsync(CancellationToken.None).GetAsyncEnumerator();
        await NextAsync(first);
        await NextAsync(second);

        _notifier.NotifyCoordinatorChanged("a");

        Assert.Equal("a", (await NextAsync(first)).Data.CoordinatorId);
        Assert.Equal("a", (await NextAsync(second)).Data.CoordinatorId);
    }

    [Fact]
    public async Task ApplicationStopping_EndsOpenStreams()
    {
        await using var stream = _notifier.SubscribeAsync(CancellationToken.None).GetAsyncEnumerator();
        await NextAsync(stream);

        _lifetime.Stop();

        Assert.False(await stream.MoveNextAsync().AsTask().WaitAsync(Timeout));
    }

    [Fact]
    public async Task Subscribe_AfterApplicationStopping_EndsImmediately()
    {
        _lifetime.Stop();

        await using var stream = _notifier.SubscribeAsync(CancellationToken.None).GetAsyncEnumerator();

        Assert.False(await stream.MoveNextAsync().AsTask().WaitAsync(Timeout));
    }

    [Fact]
    public async Task Subscribe_Cancelled_EndsTheStream()
    {
        using var cancel = new CancellationTokenSource();
        await using var stream = _notifier.SubscribeAsync(cancel.Token).GetAsyncEnumerator();
        await NextAsync(stream);

        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.MoveNextAsync().AsTask().WaitAsync(Timeout));
    }

    private static async Task<SseItem<PanelEvent>> NextAsync(IAsyncEnumerator<SseItem<PanelEvent>> stream)
    {
        Assert.True(await stream.MoveNextAsync().AsTask().WaitAsync(Timeout));
        return stream.Current;
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void Stop() => _stopping.Cancel();

        public void StopApplication() => Stop();
    }
}
