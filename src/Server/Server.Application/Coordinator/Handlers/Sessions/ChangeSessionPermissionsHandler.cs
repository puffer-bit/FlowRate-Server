using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.SessionActions.Generic;
using Shared.Permissions;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Roles;
using Server.Application.Coordinator.Sessions;
using Server.Application.Mappers;

namespace Server.Application.Coordinator.Handlers.Sessions;

/// <summary>
/// Replaces what roles may do in a session. The sender changes only the overrides of roles below its own highest
/// role, and only in permissions it has; the rest must come back as they are.
/// </summary>
public class ChangeSessionPermissionsHandler : IMessageHandler<ChangeSessionPermissionsRequest>
{
    private readonly ISessionManager _sessionManager;
    private readonly ICoordinatorPermissions _permissions;
    private readonly SessionVisibility _visibility;
    private readonly IMessageSender _messageSender;

    public ChangeSessionPermissionsHandler(ISessionManager sessionManager, ICoordinatorPermissions permissions,
        SessionVisibility visibility, IMessageSender messageSender)
    {
        _sessionManager = sessionManager;
        _permissions = permissions;
        _visibility = visibility;
        _messageSender = messageSender;
    }

    public async Task<HandleMessageResult> HandleMessage(ChangeSessionPermissionsRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var result = await ChangeAsync(request);
        _messageSender.SendMessageById(connectionId, request.ToResponse(result));

        return HandleMessageResult.NoError;
    }

    public BaseMessage? CreateFailureResponse(ChangeSessionPermissionsRequest request) =>
        request.ToResponse(ChangeSessionPermissionsResult.InternalError);

    private async Task<ChangeSessionPermissionsResult> ChangeAsync(ChangeSessionPermissionsRequest request)
    {
        if (!_sessionManager.TryGetSessionModel(request.SessionId, out var session))
            return ChangeSessionPermissionsResult.SessionNotExists;

        var overrides = request.Overrides.ToModels();
        if (overrides.DistinctBy(o => o.RoleId).Count() != overrides.Count)
            return ChangeSessionPermissionsResult.NotAllowed;

        var roles = _permissions.Roles;
        if (overrides.Any(o => roles.Find(o.RoleId) == null))
            return ChangeSessionPermissionsResult.RoleNotExists;

        var senderPosition = await _permissions.HighestPositionOfAsync(request.SenderId);
        var senderPermissions = _permissions.PermissionsOf(request.SenderId);
        var before = session.PermissionOverrides.ToDictionary(o => o.RoleId);
        var after = overrides.ToDictionary(o => o.RoleId);
        foreach (var roleId in before.Keys.Union(after.Keys))
        {
            // The override of a deleted role goes with it
            if (roles.Find(roleId) is not { } role)
                continue;

            var old = before.GetValueOrDefault(roleId, new PermissionOverride(roleId, Permission.None, Permission.None));
            var changed = after.GetValueOrDefault(roleId, new PermissionOverride(roleId, Permission.None, Permission.None));
            var changedFlags = (old.Allow ^ changed.Allow) | (old.Deny ^ changed.Deny);
            if (changedFlags == Permission.None)
                continue;

            if (role.Position >= senderPosition || (changedFlags & ~senderPermissions) != 0)
                return ChangeSessionPermissionsResult.NotAllowed;
        }

        var result = await _sessionManager.SetPermissionsAsync(session.Id, overrides);
        if (result == ChangeSessionPermissionsResult.NoError)
            _visibility.Reconfigured(session);

        return result;
    }
}
