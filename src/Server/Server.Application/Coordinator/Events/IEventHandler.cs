using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Sessions.Hybrid;
namespace Server.Application.Coordinator.Events;

public interface IEventHandler<in TEvent>
{
    Task HandleAsync(TEvent evt, CancellationToken ct = default);
}