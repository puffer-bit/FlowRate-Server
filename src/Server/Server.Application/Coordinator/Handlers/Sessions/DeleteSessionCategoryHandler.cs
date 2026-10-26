using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.SessionActions.Generic;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Layout;

namespace Server.Application.Coordinator.Handlers.Sessions;

public class DeleteSessionCategoryHandler : IMessageHandler<DeleteSessionCategoryRequest>
{
    private readonly ISessionLayoutService _layout;
    private readonly IMessageSender _messageSender;

    public DeleteSessionCategoryHandler(ISessionLayoutService layout, IMessageSender messageSender)
    {
        _layout = layout;
        _messageSender = messageSender;
    }

    public async Task<HandleMessageResult> HandleMessage(DeleteSessionCategoryRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var result = await _layout.DeleteCategoryAsync(request.CategoryId);
        _messageSender.SendMessageById(connectionId, request.ToResponse(result));

        return HandleMessageResult.NoError;
    }

    public BaseMessage? CreateFailureResponse(DeleteSessionCategoryRequest request) =>
        request.ToResponse(SessionLayoutResult.InternalError);
}
