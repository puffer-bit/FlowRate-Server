using Microsoft.Extensions.Logging;
using Shared.Enums;
using Shared.Enums.Coordinator;
using Shared.Models;
using Shared.Models.Requests.Coordinator;
using Server.Application.Coordinator.Messaging;
using Server.Application.Mappers;
using Server.Application.Orchestrator.Settings;

namespace Server.Application.Coordinator.Handlers.Settings;

/// <summary>
/// Changes the settings of this coordinator. The change is applied and announced as one from the panel is,
/// so a new address or port restarts the coordinator and the answer may not reach the sender.
/// </summary>
public class CoordinatorUpdateSettingsHandler : IMessageHandler<CoordinatorUpdateSettingsRequest>
{
    private readonly ILogger _logger;
    private readonly IMessageSender _messageSender;
    private readonly ICoordinatorSettingsService _settingsService;
    private readonly string _coordinatorInstanceId;

    public CoordinatorUpdateSettingsHandler(
        ILogger logger,
        IMessageSender messageSender,
        ICoordinatorSettingsService settingsService,
        string coordinatorInstanceId)
    {
        _logger = logger;
        _messageSender = messageSender;
        _settingsService = settingsService;
        _coordinatorInstanceId = coordinatorInstanceId;
    }

    public async Task<HandleMessageResult> HandleMessage(CoordinatorUpdateSettingsRequest? request,
        string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var result = await _settingsService.UpdateAsync(_coordinatorInstanceId, request.Settings.ToChange(),
            request.ExpectedRevision);

        var code = result.Status switch
        {
            CoordinatorSettingsUpdateStatus.Updated => CoordinatorSettingsResult.NoError,
            CoordinatorSettingsUpdateStatus.Conflict => CoordinatorSettingsResult.Conflict,
            CoordinatorSettingsUpdateStatus.Invalid => CoordinatorSettingsResult.InvalidSettings,
            _ => CoordinatorSettingsResult.InternalError
        };

        _messageSender.SendMessageById(connectionId, request.ToResponse(code, result.Settings?.ToSettingsDto(),
            result.Errors.Count > 0 ? result.Errors.ToDictionary() : null));

        if (code != CoordinatorSettingsResult.NoError)
            _logger.LogTrace("User {SenderId} failed to change the coordinator settings: {Result}",
                request.SenderId, code);

        return HandleMessageResult.NoError;
    }

    public BaseMessage? CreateFailureResponse(CoordinatorUpdateSettingsRequest request) =>
        request.ToResponse(CoordinatorSettingsResult.InternalError);
}
