using Microsoft.Extensions.Logging;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Users;
using Shared.Models;
using Shared.Models.Requests.Coordinator;

namespace Server.Application.Coordinator.Handlers.Users;

public class SetIdleHandler : IMessageHandler<CoordinatorSetIdleRequest>
{
    private readonly ILogger _logger;
    private readonly IMessageSender _messageSender;
    private readonly IUserManager _userManager;

    public SetIdleHandler(ILogger logger, IMessageSender messageSender, IUserManager userManager)
    {
        _logger = logger;
        _messageSender = messageSender;
        _userManager = userManager;
    }

    public Task<HandleMessageResult> HandleMessage(CoordinatorSetIdleRequest? request, string connectionId)
    {
        if (request == null)
            return Task.FromResult(HandleMessageResult.ForbiddenRequest);

        var result = _userManager.SetIdleLevel(request.SenderId, connectionId, request.Level);
        _messageSender.SendMessageById(connectionId, request.Answer(result, request.ToResponse));

        if (result != null)
            _logger.LogTrace("User {SenderId} failed to set the idle level {Level}: {Result}", request.SenderId,
                request.Level, result);

        return Task.FromResult(HandleMessageResult.NoError);
    }
}
