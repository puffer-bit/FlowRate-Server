using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.SessionActions.Generic;
using Server.Application.Mappers;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Users;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Authentication;

namespace Server.Application.Coordinator.Handlers.Sessions;

public class CreateSessionHandler : IMessageHandler<CreateSessionRequest>
{
    private readonly ISessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly IUserManager _users;

    public CreateSessionHandler(ISessionManager sessionManager, IMessageSender messageSender, IUserManager users)
    {
        _sessionManager = sessionManager;
        _users = users;
        _messageSender = messageSender;
    }

    public async Task<HandleMessageResult> HandleMessage(CreateSessionRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        if (request.CreateSessionModel.SessionData is Shared.Models.Requests.SessionActions.Generic.Models.SessionData.ChatSessionCreateData
            { Kind: not ChatKind.ServerChannel } data)
        {
            var creator = await _users.GetUserAsync(request.SenderId);
            foreach (var id in data.MemberIds ?? [])
                if (id <= 0 || await _users.GetUserAsync(id) is not { } user || user.IsSystem
                    || creator == null || user.CoordinatorInstanceId != creator.CoordinatorInstanceId)
                {
                    _messageSender.SendMessageById(connectionId, request.ToError(ErrorCode.InvalidValue));
                    return HandleMessageResult.NoError;
                }
        }
        var (result, session) = await _sessionManager.CreateSessionAsync(request.CreateSessionModel, request.SenderId);

        _messageSender.SendMessageById(connectionId, request.Answer(result,
            () => request.ToResponse(session!.ToDto())));

        return HandleMessageResult.NoError;
    }
}
