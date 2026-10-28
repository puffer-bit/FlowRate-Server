using Microsoft.Extensions.Logging;
using Server.Application.Abstractions.Persistence;
using Server.Application.Abstractions.Push;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Push;
using Server.Core.Models.Push;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.Coordinator;

namespace Server.Application.Coordinator.Handlers.Push;

public class RegisterPushTokenHandler : IMessageHandler<RegisterPushTokenRequest>
{
    private readonly ILogger _logger;
    private readonly IMessageSender _messageSender;
    private readonly IPushSender _pushSender;
    private readonly IPushDeviceRepository _devices;
    private readonly string _coordinatorInstanceId;

    public RegisterPushTokenHandler(
        ILogger logger,
        IMessageSender messageSender,
        IPushSender pushSender,
        IPushDeviceRepository devices,
        string coordinatorInstanceId)
    {
        _logger = logger;
        _messageSender = messageSender;
        _pushSender = pushSender;
        _devices = devices;
        _coordinatorInstanceId = coordinatorInstanceId;
    }

    public async Task<HandleMessageResult> HandleMessage(RegisterPushTokenRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        if (!_pushSender.IsEnabled)
        {
            _messageSender.SendMessageById(connectionId, request.ToError(ErrorCode.FeatureDisabled));
            return HandleMessageResult.NoError;
        }

        try
        {
            var replyToken = PushReplyToken.Create();
            await _devices.SaveAsync(new PushDeviceModel(_coordinatorInstanceId, request.SenderId, request.Token,
                request.Platform, request.KeyId.ToLowerInvariant(), Convert.FromBase64String(request.Key),
                PushReplyToken.Hash(replyToken)));

            _messageSender.SendMessageById(connectionId, request.ToResponse(replyToken));
            return HandleMessageResult.NoError;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Registering a push device of user {UserId} failed.", request.SenderId);
            _messageSender.SendMessageById(connectionId, request.ToError(ErrorCode.Internal));
            return HandleMessageResult.InternalError;
        }
    }
}
