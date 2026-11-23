using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Messaging;
using Server.Application.Mappers;
using Shared.Enums;
using Shared.Enums.Auth;
using Shared.Models;
using Shared.Models.Notifications.CoordinatorInfo;
using Shared.Models.Requests.Coordinator.Devices;

namespace Server.Application.Coordinator.Handlers.Users.Devices;

/// <summary>The devices of the sender's account.</summary>
public class AccountDevicesHandler(IMessageSender messageSender, IConnectionManager connectionManager,
    UserSessions devices) : IMessageHandler<AccountDevicesRequest>
{
    public async Task<HandleMessageResult> HandleMessage(AccountDevicesRequest? request, string connectionId)
    {
        if (request == null || !connectionManager.TryGetConnectionById(connectionId, out var connection))
            return HandleMessageResult.ForbiddenRequest;

        var list = await devices.GetAsync(request.SenderId, connection.UserSessionId);
        messageSender.SendMessageById(connectionId, request.ToResponse(list));
        return HandleMessageResult.NoError;
    }
}

/// <summary>Removes a device of the sender's account, or all of them but the current one, and disconnects them.</summary>
public class AccountDeviceRevokeHandler(IMessageSender messageSender, IConnectionManager connectionManager,
    UserSessions devices) : IMessageHandler<AccountDeviceRevokeRequest>
{
    public async Task<HandleMessageResult> HandleMessage(AccountDeviceRevokeRequest? request, string connectionId)
    {
        if (request == null || !connectionManager.TryGetConnectionById(connectionId, out var connection))
            return HandleMessageResult.ForbiddenRequest;

        var current = connection.UserSessionId;
        if (request.DeviceId is { } deviceId)
        {
            // The current device leaves with a logout, not removed from under the connection that asks
            if (deviceId == current || !await devices.EndAsync(request.SenderId, deviceId))
            {
                messageSender.SendMessageById(connectionId, request.ToError(ErrorCode.DeviceNotFound));
                return HandleMessageResult.NoError;
            }
        }
        else
        {
            await devices.EndAllButAsync(request.SenderId, current);
        }

        var list = await devices.GetAsync(request.SenderId, current);
        messageSender.SendMessageById(connectionId, request.ToResponse(list));
        return HandleMessageResult.NoError;
    }
}

/// <summary>Removes the current device: answered first, then its connection is closed.</summary>
public class LogoutHandler(IMessageSender messageSender, IConnectionManager connectionManager, UserSessions devices)
    : IMessageHandler<LogoutRequest>
{
    public async Task<HandleMessageResult> HandleMessage(LogoutRequest? request, string connectionId)
    {
        if (request == null || !connectionManager.TryGetConnectionById(connectionId, out var connection))
            return HandleMessageResult.ForbiddenRequest;

        messageSender.SendMessageById(connectionId, request.ToResponse());
        if (connection.UserSessionId is { } deviceId)
            await devices.EndAsync(request.SenderId, deviceId, DeviceEndReason.LoggedOut);
        connection.Close();
        return HandleMessageResult.NoError;
    }
}

/// <summary>What a new device waiting with a code tells about itself, for the sender to decide.</summary>
public class DeviceLinkInfoHandler(IMessageSender messageSender, DeviceLinks links)
    : IMessageHandler<DeviceLinkInfoRequest>
{
    public Task<HandleMessageResult> HandleMessage(DeviceLinkInfoRequest? request, string connectionId)
    {
        if (request == null)
            return Task.FromResult(HandleMessageResult.ForbiddenRequest);

        BaseMessage response = links.Find(request.Code) is { } link
            ? request.ToResponse(link.Info, link.Address)
            : request.ToError(ErrorCode.DeviceLinkNotFound);
        messageSender.SendMessageById(connectionId, response);
        return Task.FromResult(HandleMessageResult.NoError);
    }
}

/// <summary>
/// Adds the new device waiting with a code to the sender's account and logs it in, or turns it away.
/// </summary>
public class DeviceLinkApproveHandler(IMessageSender messageSender, IConnectionManager connectionManager,
    DeviceLinks links, UserLogin userLogin) : IMessageHandler<DeviceLinkApproveRequest>
{
    public async Task<HandleMessageResult> HandleMessage(DeviceLinkApproveRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        if (links.Find(request.Code) is not { } link || !links.Take(link)
            || !connectionManager.TryGetConnectionById(link.ConnectionId, out var waiting) || waiting.IsClosed
            || waiting.UserId != null)
        {
            messageSender.SendMessageById(connectionId, request.ToError(ErrorCode.DeviceLinkNotFound));
            return HandleMessageResult.NoError;
        }

        waiting.WaitsForDeviceLinkUntil = null;
        if (!request.Approve)
        {
            waiting.Send(new DeviceLinkDeclinedNotification());
            waiting.Close();
            messageSender.SendMessageById(connectionId, request.ToResponse());
            return HandleMessageResult.NoError;
        }

        var outcome = await userLogin.AddDeviceAsync(request.SenderId, link.Device, link.Info,
            DeviceLoginMethod.DeviceLink, waiting);
        if (outcome.Error is { } error)
        {
            waiting.Send(new DeviceLinkDeclinedNotification());
            waiting.Close();
            messageSender.SendMessageById(connectionId, request.ToError(error, outcome.BanReason));
            return HandleMessageResult.NoError;
        }

        var user = outcome.User!;
        waiting.Send(new DeviceLinkCompletedNotification(user.ToDto(user.Id), outcome.DeviceId!.Value));
        messageSender.SendMessageById(connectionId, request.ToResponse());
        return HandleMessageResult.NoError;
    }
}
