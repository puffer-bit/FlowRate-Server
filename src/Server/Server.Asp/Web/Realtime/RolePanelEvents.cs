using Server.Application.Orchestrator.Roles;

namespace Server.Asp.Web.Realtime;

/// <summary>Tells open panels about every saved role change, whoever made it.</summary>
public sealed class RolePanelEvents(IRoleService roleService, IPanelNotifier notifier) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        roleService.RolesChanged += Notify;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        roleService.RolesChanged -= Notify;
        return Task.CompletedTask;
    }

    private void Notify(RolesChange change) => notifier.NotifyCoordinatorRolesChanged(change.CoordinatorId);
}
