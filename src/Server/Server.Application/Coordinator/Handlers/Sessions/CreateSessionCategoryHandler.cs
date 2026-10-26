using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.SessionActions.Generic;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Layout;

namespace Server.Application.Coordinator.Handlers.Sessions;

public class CreateSessionCategoryHandler : IMessageHandler<CreateSessionCategoryRequest>
{
    private readonly ISessionLayoutService _layout;
    private readonly IMessageSender _messageSender;

    public CreateSessionCategoryHandler(ISessionLayoutService layout, IMessageSender messageSender)
    {
        _layout = layout;
        _messageSender = messageSender;
    }

    public async Task<HandleMessageResult> HandleMessage(CreateSessionCategoryRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var (result, categoryId) = await _layout.CreateCategoryAsync(request.Name);
        _messageSender.SendMessageById(connectionId, request.ToResponse(result, categoryId));

        return HandleMessageResult.NoError;
    }

    public BaseMessage? CreateFailureResponse(CreateSessionCategoryRequest request) =>
        request.ToResponse(SessionLayoutResult.InternalError);
}
