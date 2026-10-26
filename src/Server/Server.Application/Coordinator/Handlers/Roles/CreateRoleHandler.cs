using Server.Application.Coordinator.Messaging;
using Server.Application.Mappers;
using Server.Application.Orchestrator.Roles;
using Shared.Enums;
using Shared.Enums.Coordinator;
using Shared.Models;
using Shared.Models.Requests.Coordinator;

namespace Server.Application.Coordinator.Handlers.Roles;

public class CreateRoleHandler : IMessageHandler<CoordinatorCreateRoleRequest>
{
    private readonly IMessageSender _messageSender;
    private readonly IRoleService _roleService;
    private readonly string _coordinatorInstanceId;

    public CreateRoleHandler(IMessageSender messageSender, IRoleService roleService, string coordinatorInstanceId)
    {
        _messageSender = messageSender;
        _roleService = roleService;
        _coordinatorInstanceId = coordinatorInstanceId;
    }

    public async Task<HandleMessageResult> HandleMessage(CoordinatorCreateRoleRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var result = await _roleService.CreateAsync(_coordinatorInstanceId, request.SenderId, request.Role.ToChange());
        _messageSender.SendMessageById(connectionId, request.ToResponse(result.Status.ToResult(), result.Value?.ToDto()));

        return HandleMessageResult.NoError;
    }

    public BaseMessage? CreateFailureResponse(CoordinatorCreateRoleRequest request) =>
        request.ToResponse(CoordinatorRoleResult.InternalError);
}
