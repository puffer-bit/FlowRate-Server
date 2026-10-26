using Microsoft.Extensions.Logging;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Messaging;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.Coordinator;

namespace Server.Application.Coordinator.Handlers.Push;

public class UnregisterPushTokenHandler : IMessageHandler<UnregisterPushTokenRequest>
{
    private readonly ILogger _logger;
    private readonly IMessageSender _messageSender;
    private readonly IPushDeviceRepository _devices;
    private readonly string _coordinatorInstanceId;

    public UnregisterPushTokenHandler(
        ILogger logger,
        IMessageSender messageSender,
        IPushDeviceRepository devices,
        string coordinatorInstanceId)
    {
        _logger = logger;
        _messageSender = messageSender;
        _devices = devices;
        _coordinatorInstanceId = coordinatorInstanceId;
    }

    public async Task<HandleMessageResult> HandleMessage(UnregisterPushTokenRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        try
        {
            await _devices.RemoveAsync(_coordinatorInstanceId, request.SenderId, request.Token);

            _messageSender.SendMessageById(connectionId, request.ToResponse(PushTokenResult.NoError));
            return HandleMessageResult.NoError;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Removing a push device of user {UserId} failed.", request.SenderId);
            _messageSender.SendMessageById(connectionId, request.ToResponse(PushTokenResult.InternalError));
            return HandleMessageResult.InternalError;
        }
    }

    public BaseMessage? CreateFailureResponse(UnregisterPushTokenRequest request) =>
        request.ToResponse(PushTokenResult.InternalError);
}
