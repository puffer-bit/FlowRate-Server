namespace Server.Application.Coordinator.Events;

public interface ICoordinatorEventBus
{
    /// <summary>Hands the event to every handler of <typeparamref name="TEvent"/>; a failing handler does not stop the others.</summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default);
}
