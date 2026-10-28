using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.Coordinator;
using Server.Application.Coordinator.Messaging;
using Server.Application.Mappers;
using Server.Application.Orchestrator.SFUPool;
using Server.Application.Orchestrator.Settings;

namespace Server.Application.Coordinator.Handlers.Settings;

public class CoordinatorSettingsHandler : IMessageHandler<CoordinatorSettingsRequest>
{
    private readonly IMessageSender _messageSender;
    private readonly ICoordinatorSettingsService _settingsService;
    private readonly ISfuNodeRegistry _sfuNodes;
    private readonly string _coordinatorInstanceId;

    public CoordinatorSettingsHandler(
        IMessageSender messageSender,
        ICoordinatorSettingsService settingsService,
        ISfuNodeRegistry sfuNodes,
        string coordinatorInstanceId)
    {
        _sfuNodes = sfuNodes;
        _messageSender = messageSender;
        _settingsService = settingsService;
        _coordinatorInstanceId = coordinatorInstanceId;
    }

    public async Task<HandleMessageResult> HandleMessage(CoordinatorSettingsRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var model = await _settingsService.GetAsync(_coordinatorInstanceId);
        var nodes = await _settingsService.GetSfuNodesAsync();

        _messageSender.SendMessageById(connectionId, model == null
            ? request.ToError(ErrorCode.Internal)
            : request.ToResponse(model.ToSettingsDto(),
                // A snapshot of the connections: the client shows it as it was when the settings were opened
                nodes.Select(node => node.ToDto(node.IsEnabled ? _sfuNodes.GetStatus(node.Id) : null)).ToList()));

        return HandleMessageResult.NoError;
    }
}
