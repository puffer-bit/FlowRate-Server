using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.SessionActions.Generic;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Layout;

namespace Server.Application.Coordinator.Handlers.Sessions;

public class MoveSessionCategoryHandler : IMessageHandler<MoveSessionCategoryRequest>
{
    private readonly ISessionLayoutService _layout;
    private readonly IMessageSender _messageSender;

    public MoveSessionCategoryHandler(ISessionLayoutService layout, IMessageSender messageSender)
    {
        _layout = layout;
        _messageSender = messageSender;
    }

    public async Task<HandleMessageResult> HandleMessage(MoveSessionCategoryRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var result = await _layout.MoveCategoryAsync(request.CategoryId, request.Index);
        _messageSender.SendMessageById(connectionId, request.ToResponse(result));

        return HandleMessageResult.NoError;
    }

    public BaseMessage? CreateFailureResponse(MoveSessionCategoryRequest request) =>
        request.ToResponse(SessionLayoutResult.InternalError);
}
