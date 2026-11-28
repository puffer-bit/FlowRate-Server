using Microsoft.Extensions.Logging;
using Shared.Enums;
using Shared.Models.Requests.Coordinator;
using Shared.Models.Responses.Coordinator;
using Server.Application.Mappers;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Users;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Events;

namespace Server.Application.Coordinator.Handlers.CoordinatorInfo;

public class CoordinatorInfoActionHandler : IMessageHandler<CoordinatorInfoRequest>
{
    private readonly ILogger _logger;
    private readonly ICoordinatorInstanceContext _coordinatorInstanceContext;
    private readonly IMessageSender _messageSender;
    private readonly IUserManager _userManager;
    private readonly Orchestrator.Restart.IServerRestartNotice? _restartNotice;
    
    public CoordinatorInfoActionHandler(
        ILogger logger, 
        ICoordinatorInstanceContext coordinatorInstanceContext, 
        IMessageSender messageSender, 
        IUserManager userManager,
        Orchestrator.Restart.IServerRestartNotice? restartNotice = null)
    {
        _restartNotice = restartNotice;
        _logger = logger;
        _coordinatorInstanceContext = coordinatorInstanceContext;
        _messageSender = messageSender;
        _userManager = userManager;
    }
    
    public async Task<HandleMessageResult> HandleMessage(CoordinatorInfoRequest? request, string connectionId)
    {
        if (request == null)
        {
            return HandleMessageResult.ForbiddenRequest;
        }
        
        var coordinatorInfoDto = _coordinatorInstanceContext.ToDto(_userManager.ConnectedUsersCount);

        SendResponse(request.ToResponse(coordinatorInfoDto), connectionId);
        // Every client asks for the coordinator after it logs in: a standing warning reaches it this way
        if (_restartNotice?.Warning is { } warning)
            _messageSender.SendMessageById(connectionId, warning);
        
        return HandleMessageResult.NoError;
    }
    
    private void SendResponse(CoordinatorInfoResponse response, string connectionId)
    {
        _messageSender.SendMessageById(connectionId, response);
    }
}
