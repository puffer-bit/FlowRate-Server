using System.Collections.Concurrent;
using Server.Application.Coordinator.Events;

namespace Server.Application.Tests.Fakes;

/// <summary>
/// Records published events by their static type, the way the real bus dispatches them:
/// an event published as its base type would never reach a handler, and is not found by <see cref="Of{TEvent}"/>.
/// </summary>
internal sealed class RecordingEventBus : ICoordinatorEventBus
{
    private readonly ConcurrentQueue<(Type PublishedAs, object Event)> _events = new();

    public IReadOnlyList<object> Events => _events.Select(e => e.Event).ToList();

    public IReadOnlyList<TEvent> Of<TEvent>() =>
        _events.Where(e => e.PublishedAs == typeof(TEvent)).Select(e => (TEvent)e.Event).ToList();

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
    {
        _events.Enqueue((typeof(TEvent), @event!));
        return Task.CompletedTask;
    }
}
