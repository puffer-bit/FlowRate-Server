using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Roles;
using Server.Application.Mappers;
using Shared.Enums;
using Shared.Enums.Coordinator;
using Shared.Models;
using Shared.Models.Requests.Coordinator;

namespace Server.Application.Coordinator.Handlers.Roles;

public class CoordinatorRolesHandler : IMessageHandler<CoordinatorRolesRequest>
{
    private readonly IMessageSender _messageSender;
    private readonly ICoordinatorPermissions _permissions;

    public CoordinatorRolesHandler(IMessageSender messageSender, ICoordinatorPermissions permissions)
    {
        _messageSender = messageSender;
        _permissions = permissions;
    }

    public Task<HandleMessageResult> HandleMessage(CoordinatorRolesRequest? request, string connectionId)
    {
        if (request == null)
            return Task.FromResult(HandleMessageResult.ForbiddenRequest);

        _messageSender.SendMessageById(connectionId, request.ToResponse(CoordinatorRoleResult.NoError,
            _permissions.Roles.ToDtos(), _permissions.PermissionsOf(request.SenderId)));

        return Task.FromResult(HandleMessageResult.NoError);
    }

    public BaseMessage? CreateFailureResponse(CoordinatorRolesRequest request) =>
        request.ToResponse(CoordinatorRoleResult.InternalError);
}
