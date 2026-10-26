using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Users;

namespace Server.Asp.Web.Realtime;

/// <summary>Turns everything that changes what the panel shows for one coordinator into a change signal.</summary>
public sealed class CoordinatorPanelEvents(string coordinatorId, IPanelNotifier notifier) :
    IEventHandler<HybridSessionUpdatedEvent>,
    IEventHandler<SessionsUpdatedEvent>
{
    public Task HandleAsync(HybridSessionUpdatedEvent evt, CancellationToken ct = default) => Notify();

    public Task HandleAsync(SessionsUpdatedEvent evt, CancellationToken ct = default) => Notify();

    public void Watch(IUserManager userManager)
    {
        userManager.UserConnected += _ => Notify();
        userManager.UserUpdated += _ => Notify();
        userManager.UserDisconnected += (_, _) => Notify();
        userManager.UserDeleted += _ => Notify();
    }

    private Task Notify()
    {
        notifier.NotifyCoordinatorChanged(coordinatorId);
        return Task.CompletedTask;
    }
}
