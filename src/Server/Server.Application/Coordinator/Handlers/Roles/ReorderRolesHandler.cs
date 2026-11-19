using Server.Application.Coordinator.Messaging;
using Server.Application.Mappers;
using Server.Application.Orchestrator.Roles;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.Coordinator;

namespace Server.Application.Coordinator.Handlers.Roles;

public class ReorderRolesHandler : IMessageHandler<CoordinatorReorderRolesRequest>
{
    private readonly IMessageSender _messageSender;
    private readonly IRoleService _roleService;
    private readonly string _coordinatorInstanceId;

    public ReorderRolesHandler(IMessageSender messageSender, IRoleService roleService, string coordinatorInstanceId)
    {
        _messageSender = messageSender;
        _roleService = roleService;
        _coordinatorInstanceId = coordinatorInstanceId;
    }

    public async Task<HandleMessageResult> HandleMessage(CoordinatorReorderRolesRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var result = await _roleService.ReorderAsync(_coordinatorInstanceId, request.SenderId, request.RoleIds.ToList());
        _messageSender.SendMessageById(connectionId, request.Answer(result.Status.ToResult(),
            () => request.ToResponse(result.Value!.ToDtos())));

        return HandleMessageResult.NoError;
    }
}
