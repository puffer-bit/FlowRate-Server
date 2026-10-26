using Microsoft.Extensions.Logging;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Authentication;

namespace Server.Application.Coordinator.Events;

public class CoordinatorEventBus : ICoordinatorEventBus
{
    private readonly ILogger _logger;

    // Each object implements IEventHandler<T> for one or more event types
    private readonly IReadOnlyList<object> _handlers;

    public CoordinatorEventBus(ILogger logger, IEnumerable<object> handlers)
    {
        _logger = logger;
        _handlers = handlers.ToList();
    }

    public async Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default)
    {
        foreach (var handler in _handlers.OfType<IEventHandler<TEvent>>())
        {
            try
            {
                await handler.HandleAsync(evt, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Handling {EventType} by {Handler} failed.", typeof(TEvent).Name,
                    handler.GetType().Name);
            }
        }
    }
}
