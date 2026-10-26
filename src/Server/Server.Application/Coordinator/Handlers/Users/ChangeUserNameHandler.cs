using Microsoft.Extensions.Logging;
using Server.Application.Mappers;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Requests.Coordinator;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Users;

namespace Server.Application.Coordinator.Handlers.Users;

public class ChangeUserNameHandler : IMessageHandler<CoordinatorChangeUsernameRequest>
{
    private readonly ILogger _logger;
    
    private readonly IMessageSender _messageSender;
    private readonly IUserManager _userManager;

    public ChangeUserNameHandler(
        ILogger logger,
        IMessageSender messageSender,
        IUserManager userManager)
    {
        _messageSender = messageSender;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<HandleMessageResult> HandleMessage(CoordinatorChangeUsernameRequest? request, string connectionId)
    {
        if (request == null) 
        {
            return HandleMessageResult.ForbiddenRequest;
        }

        var userId = request.UserId ?? request.SenderId;

        var result = await _userManager.ChangeUsername(userId, request.Username);
        var user = result == ChangeUsernameResult.NoError
            ? (await _userManager.GetUserAsync(userId))?.ToDto(request.SenderId)
            : null;

        _messageSender.SendMessageById(connectionId, request.ToResponse(result, user));
        
        if (result != ChangeUsernameResult.NoError)
            _logger.LogTrace("User {SenderId} failed to rename user {Id}: {Result}", request.SenderId, userId, result);
        
        return HandleMessageResult.NoError;
    }

    public BaseMessage? CreateFailureResponse(CoordinatorChangeUsernameRequest request) =>
        request.ToResponse(ChangeUsernameResult.InternalError);
}
