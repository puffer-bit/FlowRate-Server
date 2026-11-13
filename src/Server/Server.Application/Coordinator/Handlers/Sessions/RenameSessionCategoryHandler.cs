using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.SessionActions.Generic;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Layout;

namespace Server.Application.Coordinator.Handlers.Sessions;

public class RenameSessionCategoryHandler : IMessageHandler<RenameSessionCategoryRequest>
{
    private readonly ISessionLayoutService _layout;
    private readonly IMessageSender _messageSender;

    public RenameSessionCategoryHandler(ISessionLayoutService layout, IMessageSender messageSender)
    {
        _layout = layout;
        _messageSender = messageSender;
    }

    public async Task<HandleMessageResult> HandleMessage(RenameSessionCategoryRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var result = await _layout.RenameCategoryAsync(request.CategoryId, request.Name);
        _messageSender.SendMessageById(connectionId, request.Answer(result, request.ToResponse));

        return HandleMessageResult.NoError;
    }
}
