using Microsoft.Extensions.Logging.Abstractions;
using Server.Application.Coordinator.Events;

namespace Server.Application.Tests.Coordinator;

public class CoordinatorEventBusTests
{
    private record Ping(int Value);
    private record Pong;

    private sealed class Recorder : IEventHandler<Ping>
    {
        public List<int> Received { get; } = [];

        public Task HandleAsync(Ping evt, CancellationToken ct = default)
        {
            Received.Add(evt.Value);
            return Task.CompletedTask;
        }
    }

    private sealed class Failing : IEventHandler<Ping>
    {
        public Task HandleAsync(Ping evt, CancellationToken ct = default) => throw new InvalidOperationException();
    }

    [Fact]
    public async Task Publish_ReachesEveryHandlerOfTheEvent()
    {
        var first = new Recorder();
        var second = new Recorder();
        var bus = new CoordinatorEventBus(NullLogger.Instance, [first, second]);

        await bus.PublishAsync(new Ping(1));

        Assert.Equal([1], first.Received);
        Assert.Equal([1], second.Received);
    }

    [Fact]
    public async Task Publish_FailingHandler_DoesNotStopTheOthers()
    {
        var recorder = new Recorder();
        var bus = new CoordinatorEventBus(NullLogger.Instance, [new Failing(), recorder]);

        await bus.PublishAsync(new Ping(1));

        Assert.Equal([1], recorder.Received);
    }

    [Fact]
    public async Task Publish_EventWithoutHandlers_IsIgnored()
    {
        var recorder = new Recorder();
        var bus = new CoordinatorEventBus(NullLogger.Instance, [recorder]);

        await bus.PublishAsync(new Pong());

        Assert.Empty(recorder.Received);
    }
}
