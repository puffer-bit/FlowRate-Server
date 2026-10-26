using Microsoft.Extensions.Logging;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Users;
using Server.Application.Mappers;
using Server.Application.Orchestrator.Roles;
using Server.Core.Models;
using Server.Core.Models.Roles;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models.Notifications.CoordinatorInfo;
using Shared.Permissions;

namespace Server.Application.Coordinator.Roles;

/// <summary>
/// The roles of this coordinator: answers what its users may do and brings role changes, wherever they were
/// made, to the connected users.
/// </summary>
public sealed class CoordinatorRoles : ICoordinatorPermissions, IDisposable
{
    private readonly ILogger _logger;
    private readonly IRoleService _roleService;
    private readonly IUserManager _userManager;
    private readonly IMessageSender _messageSender;
    private readonly string _coordinatorInstanceId;

    public CoordinatorRoles(ILogger logger, IRoleService roleService, IUserManager userManager,
        IMessageSender messageSender, string coordinatorInstanceId)
    {
        _logger = logger;
        _roleService = roleService;
        _userManager = userManager;
        _messageSender = messageSender;
        _coordinatorInstanceId = coordinatorInstanceId;
    }

    public RoleSet Roles => _roleService.GetLoaded(_coordinatorInstanceId);

    /// <summary>Loads the roles; requests are checked against them from then on.</summary>
    public async Task LoadAsync()
    {
        await _roleService.LoadAsync(_coordinatorInstanceId);
        _roleService.RolesChanged -= OnRolesChanged;
        _roleService.RolesChanged += OnRolesChanged;
    }

    public Permission PermissionsOf(int userId)
    {
        if (userId == UserModel.SystemUserId)
            return Permission.All;

        return _userManager.TryGetConnectedUser(userId, out var user)
            ? Roles.PermissionsOf(user!.RoleIds)
            : Roles.Everyone.Permissions;
    }

    public bool Has(int userId, Permission permission) => (PermissionsOf(userId) & permission) == permission;

    public event Action<IReadOnlyCollection<int>>? PermissionsChanged;

    public Permission PermissionsIn(int userId, SessionModel session)
    {
        if (userId == UserModel.SystemUserId)
            return Permission.All;

        return Resolve(session, _userManager.TryGetConnectedUser(userId, out var user) ? user!.RoleIds : []);
    }

    public async Task<Permission> PermissionsInAsync(int userId, SessionModel session)
    {
        if (userId == UserModel.SystemUserId)
            return Permission.All;

        var user = _userManager.TryGetConnectedUser(userId, out var connected)
            ? connected
            : await _userManager.GetUserAsync(userId);
        return Resolve(session, user?.RoleIds ?? []);
    }

    private Permission Resolve(SessionModel session, IReadOnlyCollection<int> roleIds)
    {
        var roles = Roles;
        return SessionPermissions.Resolve(roles.PermissionsOf(roleIds), session.PermissionOverrides,
            roles.Everyone.Id, roleIds);
    }

    public bool HasIn(int userId, SessionModel session, Permission permission) =>
        (PermissionsIn(userId, session) & permission) == permission;

    public bool IsFrozen(int userId) =>
        _userManager.TryGetConnectedUser(userId, out var user) && user!.State == UserState.Suspended;

    public async Task<int> HighestPositionOfAsync(int userId)
    {
        if (userId == UserModel.SystemUserId)
            return int.MaxValue;

        var user = await _userManager.GetUserAsync(userId);
        return Roles.HighestPositionOf(user?.RoleIds ?? []);
    }

    private void OnRolesChanged(RolesChange change)
    {
        if (change.CoordinatorId != _coordinatorInstanceId)
            return;

        try
        {
            foreach (var (userId, roleIds) in change.UserRoles)
                _userManager.SetRoles(userId, roleIds);

            if (change.RolesModified)
                _messageSender.BroadcastMessage(new CoordinatorRolesChangedNotification(change.Roles.ToDtos()));

            // A changed role may change what anyone may do, a changed user only what they may do
            var affected = change.RolesModified
                ? _userManager.GetAllConnectedUsers().Keys
                : change.UserRoles.Keys.Where(userId => _userManager.TryGetConnectedUser(userId, out _));
            var users = affected.Where(userId => userId != UserModel.SystemUserId).ToList();
            foreach (var userId in users)
                _messageSender.SendMessageByUserId(userId,
                    new CoordinatorPermissionsChangedNotification(PermissionsOf(userId)));
            PermissionsChanged?.Invoke(users);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Announcing a change of the roles failed.");
        }
    }

    public void Dispose() => _roleService.RolesChanged -= OnRolesChanged;
}
