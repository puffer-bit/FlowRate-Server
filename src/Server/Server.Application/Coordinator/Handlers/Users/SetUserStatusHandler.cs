using Microsoft.Extensions.Logging;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Users;
using Server.Application.Mappers;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Requests.Coordinator;

namespace Server.Application.Coordinator.Handlers.Users;

public class SetUserStatusHandler : IMessageHandler<CoordinatorSetUserStatusRequest>
{
    private readonly ILogger _logger;
    private readonly IMessageSender _messageSender;
    private readonly IUserManager _userManager;

    public SetUserStatusHandler(ILogger logger, IMessageSender messageSender, IUserManager userManager)
    {
        _logger = logger;
        _messageSender = messageSender;
        _userManager = userManager;
    }

    public async Task<HandleMessageResult> HandleMessage(CoordinatorSetUserStatusRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var result = await _userManager.SetStatus(request.SenderId, request.Status);
        var user = result == null && _userManager.TryGetConnectedUser(request.SenderId, out var updated)
            ? updated!.ToDto(request.SenderId)
            : null;

        _messageSender.SendMessageById(connectionId, request.Answer(result, () => request.ToResponse(user!)));

        if (result != null)
            _logger.LogTrace("User {SenderId} failed to set the status {Status}: {Result}", request.SenderId,
                request.Status, result);

        return HandleMessageResult.NoError;
    }
}
