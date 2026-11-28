using Microsoft.Extensions.Logging;
using Server.Application.Mappers;
using Shared.Enums;
using Shared.Models;
using Shared.Models.DataTransferObjects;
using Shared.Models.Requests.Coordinator;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Users;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Events;

namespace Server.Application.Coordinator.Handlers.Users;

public class UserActionHandler : IMessageHandler<CoordinatorUserInfoRequest>
{
    private readonly ILogger _logger;
    
    private readonly IMessageSender _messageSender;
    private readonly IUserManager _userManager;

    public UserActionHandler(
        ILogger logger,
        IMessageSender messageSender, 
        IUserManager userManager)
    {
        _messageSender = messageSender;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<HandleMessageResult> HandleMessage(CoordinatorUserInfoRequest? request, string connectionId)
    {
        if (request == null) 
        {
            return HandleMessageResult.ForbiddenRequest;
        }

        await HandleUsersInfoAsync(request, connectionId);
        return HandleMessageResult.NoError;
    }

    private async Task HandleUsersInfoAsync(CoordinatorUserInfoRequest request, string connectionId)
    {
        var usersList = new Dictionary<int, UserDTO>();
        
        foreach (var userId in request.UserIds)
        {
            if (await _userManager.GetUserAsync(userId) is { } user)
            {
                usersList.Add(userId, user.ToDto(request.SenderId));
            }
        }

        if (usersList.Count == 0) 
        {
            _messageSender.SendMessageById(connectionId, 
                request.ToResponse(usersList));
            
        }
        else
        {
            var responseData = request.ToResponse(usersList);
        
            _messageSender.SendMessageById(connectionId, 
                responseData);
        }
    }
}
