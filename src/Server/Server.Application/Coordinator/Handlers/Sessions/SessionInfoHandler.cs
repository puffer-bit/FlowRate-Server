using Shared.Enums;
using Shared.Models;
using Shared.Models.DataTransferObjects;
using Shared.Models.Requests.SessionActions.Generic;
using Server.Application.Mappers;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Layout;
using Server.Application.Coordinator.Users;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Authentication;

namespace Server.Application.Coordinator.Handlers.Sessions;

public class SessionInfoHandler : IMessageHandler<SessionInfoRequest>
{
    private readonly SessionVisibility _visibility;
    private readonly ISessionLayoutService _layout;
    private readonly IMessageSender _messageSender;

    public SessionInfoHandler(SessionVisibility visibility, ISessionLayoutService layout, IMessageSender messageSender)
    {
        _visibility = visibility;
        _layout = layout;
        _messageSender = messageSender;
    }

    public Task<HandleMessageResult> HandleMessage(SessionInfoRequest? request, string connectionId)
    {
        if (request == null)
            return Task.FromResult(HandleMessageResult.ForbiddenRequest);

        _messageSender.SendMessageById(connectionId, request.SessionId is { } sessionId
            ? SingleSession(request, sessionId)
            : AllSessions(request));

        return Task.FromResult(HandleMessageResult.NoError);
    }

    private BaseMessage AllSessions(SessionInfoRequest request)
    {
        var layout = _layout.GetLayout();
        // The layout names sessions the user may not see too; the client places only those it has
        var sessions = _visibility.ShowAll(request.SenderId).ToDictionary(session => session.Id, session => session.ToDto());
        return request.ToResponse(sessions, layout);
    }

    private BaseMessage SingleSession(SessionInfoRequest request, int sessionId)
    {
        // A session the user may not see is one that does not exist for it
        if (_visibility.Show(request.SenderId, sessionId) is not { } session)
            return request.ToError(ErrorCode.SessionNotFound);

        return request.ToResponse(new Dictionary<int, SessionDTO> { [session.Id] = session.ToDto() });
    }
}
