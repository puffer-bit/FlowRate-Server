using Server.Application.Coordinator.Messaging;
using Server.Application.Mappers;
using Server.Application.Orchestrator.Roles;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.Coordinator;

namespace Server.Application.Coordinator.Handlers.Roles;

public class UnassignRoleHandler : IMessageHandler<CoordinatorUnassignRoleRequest>
{
    private readonly IMessageSender _messageSender;
    private readonly IRoleService _roleService;
    private readonly string _coordinatorInstanceId;

    public UnassignRoleHandler(IMessageSender messageSender, IRoleService roleService, string coordinatorInstanceId)
    {
        _messageSender = messageSender;
        _roleService = roleService;
        _coordinatorInstanceId = coordinatorInstanceId;
    }

    public async Task<HandleMessageResult> HandleMessage(CoordinatorUnassignRoleRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var result = await _roleService.UnassignAsync(_coordinatorInstanceId, request.SenderId, request.TargetUserId,
            request.RoleId);
        _messageSender.SendMessageById(connectionId, request.Answer(result.Status.ToResult(),
            () => request.ToResponse(result.Value!.ToList())));

        return HandleMessageResult.NoError;
    }
}
