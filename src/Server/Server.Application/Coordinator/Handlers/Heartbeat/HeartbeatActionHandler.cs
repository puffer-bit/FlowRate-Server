using Shared.Enums;
using Shared.Models.Requests.Heartbeat;
using Server.Application.Coordinator.Heartbeat;
using Server.Application.Coordinator.Messaging;

namespace Server.Application.Coordinator.Handlers.Heartbeat;

public class HeartbeatActionHandler : IMessageHandler<HeartbeatPingRequest>
{
    private readonly IHeartbeatManager _heartbeatManager;
    private readonly IMessageSender _messageSender;

    public HeartbeatActionHandler(IHeartbeatManager heartbeatManager, IMessageSender messageSender)
    {
        _heartbeatManager = heartbeatManager;
        _messageSender = messageSender;
    }

    public Task<HandleMessageResult> HandleMessage(HeartbeatPingRequest? request, string connectionId)
    {
        if (request == null)
            return Task.FromResult(HandleMessageResult.ForbiddenRequest);

        _heartbeatManager.RegisterHeartbeat(connectionId);
        _messageSender.SendMessageById(connectionId, request.ToResponse(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        return Task.FromResult(HandleMessageResult.NoError);
    }
}
