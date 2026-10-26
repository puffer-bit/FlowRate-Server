using Server.Application.Coordinator.Media;
using Server.Application.Coordinator.Messaging;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.Coordinator;

namespace Server.Application.Coordinator.Handlers.Media;

public class MediaTokenHandler : IMessageHandler<MediaTokenRequest>
{
    private readonly IMessageSender _messageSender;
    private readonly IMediaService _media;

    public MediaTokenHandler(IMessageSender messageSender, IMediaService media)
    {
        _messageSender = messageSender;
        _media = media;
    }

    public Task<HandleMessageResult> HandleMessage(MediaTokenRequest? request, string connectionId)
    {
        if (request == null)
            return Task.FromResult(HandleMessageResult.ForbiddenRequest);

        var (token, expiresAt) = _media.IssueToken(request.SenderId);
        _messageSender.SendMessageById(connectionId, request.ToResponse(MediaTokenResult.NoError, token, expiresAt));
        return Task.FromResult(HandleMessageResult.NoError);
    }

    public BaseMessage? CreateFailureResponse(MediaTokenRequest request) =>
        request.ToResponse(MediaTokenResult.InternalError);
}
