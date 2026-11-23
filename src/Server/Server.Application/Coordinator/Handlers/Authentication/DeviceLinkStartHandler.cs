using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Messaging;
using Shared.Enums;
using Shared.Models.Requests.Auth;

namespace Server.Application.Coordinator.Handlers.Authentication;

/// <summary>
/// A new device asks to be added by a device of an account: it gets a code to show and waits, logged out, until the
/// code is approved, declined or expires.
/// </summary>
public class DeviceLinkStartHandler(LoginReplies replies, IAuthenticator authenticator, UserLogin userLogin,
    DeviceLinks links) : IMessageHandler<DeviceLinkStartRequest>
{
    public async Task<HandleMessageResult> HandleMessage(DeviceLinkStartRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;
        if (replies.LoggedOutConnection(request, connectionId) is not { } connection)
            return HandleMessageResult.ForbiddenRequest;

        if (!authenticator.CheckLoginData(request.ServerPassword))
            return replies.Reject(request, connection);
        if (NewDevice.From(request.NewDevice) is not { } device
            || await userLogin.Devices.IsPublicKeyTakenAsync(device.PublicKey))
            return replies.Reject(request, connection, ErrorCode.InvalidValue);

        var link = links.Start(connectionId, device, request.Device, connection.IpEndpoint);
        connection.WaitsForDeviceLinkUntil = link.ExpiresAt;
        replies.Send(connectionId, request.ToResponse(DeviceLinks.Format(link.Code), link.ExpiresAt));
        return HandleMessageResult.NoError;
    }
}
