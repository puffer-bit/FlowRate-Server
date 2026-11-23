using Microsoft.Extensions.Logging;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Messaging;
using Server.Core.Models.User;
using Shared.Auth;
using Shared.Enums;
using Shared.Enums.Auth;
using Shared.Models.DataTransferObjects;
using Shared.Models.Requests.Auth;

namespace Server.Application.Coordinator.Handlers.Authentication;

/// <summary>
/// Logs a connection in with one of the methods of <see cref="LoginMethodDTO"/>. The server password is asked of every
/// login. A login by username, or with an identity of the earlier protocol 3 whose user has factors, goes on in
/// <see cref="SecondStageAuthentication"/>; every other method answers at once. A failed login closes the connection.
/// </summary>
public class LoginHandler(
    ILogger logger,
    LoginReplies replies,
    IAuthenticator authenticator,
    UserLogin userLogin,
    LoginNonces nonces,
    PendingLogins pendingLogins,
    IUserRepository users,
    IUserLoginCodeRepository loginCodes,
    string coordinatorInstanceId,
    TimeProvider timeProvider) : IMessageHandler<LoginRequest>
{
    public async Task<HandleMessageResult> HandleMessage(LoginRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;
        if (replies.LoggedOutConnection(request, connectionId) is not { } connection)
            return HandleMessageResult.ForbiddenRequest;

        try
        {
            if (!authenticator.CheckLoginData(request.ServerPassword))
                return replies.Reject(request, connection);

            return request.Method switch
            {
                DeviceLoginDTO device => await LoginWithDeviceAsync(request, device, connection),
                RegisterLoginDTO register => await RegisterAsync(request, register, connection),
                RecoveryLoginDTO recovery => await LoginWithRecoveryKeyAsync(request, recovery, connection),
                UsernameLoginDTO username => await LoginWithUsernameAsync(request, username, connection),
                LoginCodeLoginDTO code => await LoginWithCodeAsync(request, code, connection),
                LegacyLoginDTO legacy => await LoginWithLegacyIdentityAsync(request, legacy, connection),
                _ => replies.Reject(request, connection, ErrorCode.InvalidRequest)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Login failed for connection {ConnectionId}", connectionId);
            return replies.Reject(request, connection, ErrorCode.Internal);
        }
    }

    private async Task<HandleMessageResult> LoginWithDeviceAsync(LoginRequest request, DeviceLoginDTO method,
        IClientConnection connection)
    {
        if (nonces.Take(connection.ConnectionId) is not { } nonce)
            return replies.Reject(request, connection);

        // An unknown device is told apart from a wrong signature: the client drops its key and logs in another way
        if (await userLogin.Devices.GetAsync(method.DeviceId) is not { PublicKey: { } publicKey } device)
            return replies.Reject(request, connection, ErrorCode.DeviceNotFound);

        var payload = LoginSignature.Payload(LoginSignature.DevicePurpose, coordinatorInstanceId, nonce);
        if (!LoginSignature.Verify(publicKey, payload, method.Signature))
        {
            logger.LogInformation("Wrong device signature for device {DeviceId} from {Ip}.", device.Id,
                connection.IpEndpoint);
            return replies.Reject(request, connection);
        }

        return Respond(request, connection, await userLogin.ResumeAsync(device, request.Device, connection));
    }

    private async Task<HandleMessageResult> RegisterAsync(LoginRequest request, RegisterLoginDTO method,
        IClientConnection connection)
    {
        if (await NewDeviceOf(method.NewDevice) is not { } device || RecoveryKeys.From(method.Keys) is not { } keys
            || await users.IsRecoveryKeyTakenAsync(keys.RecoveryKeyHash, 0))
            return replies.Reject(request, connection, ErrorCode.InvalidValue);

        return Respond(request, connection, await userLogin.RegisterAsync(keys, device, request.Device, connection));
    }

    private async Task<HandleMessageResult> LoginWithRecoveryKeyAsync(LoginRequest request, RecoveryLoginDTO method,
        IClientConnection connection)
    {
        if (nonces.Take(connection.ConnectionId) is not { } nonce)
            return replies.Reject(request, connection);
        if (await NewDeviceOf(method.NewDevice) is not { } device
            || LoginSignature.TryDecodePublicKey(method.RecoveryPublicKey) is not { } recoveryKey)
            return replies.Reject(request, connection, ErrorCode.InvalidValue);

        var user = await users.GetByRecoveryKeyHashAsync(coordinatorInstanceId, SecretHash.Of(recoveryKey));
        var payload = LoginSignature.Payload(LoginSignature.RecoveryPurpose, coordinatorInstanceId, nonce,
            method.NewDevice.PublicKey);
        if (user == null || user.IsSystem || !LoginSignature.Verify(recoveryKey, payload, method.Signature))
        {
            logger.LogInformation("Refused a login with a recovery key from {Ip}.", connection.IpEndpoint);
            return replies.Reject(request, connection);
        }

        return Respond(request, connection, await userLogin.AddDeviceAsync(user.Id, device, request.Device,
            DeviceLoginMethod.Recovery, connection));
    }

    private async Task<HandleMessageResult> LoginWithUsernameAsync(LoginRequest request, UsernameLoginDTO method,
        IClientConnection connection)
    {
        if (await NewDeviceOf(method.NewDevice) is not { } device)
            return replies.Reject(request, connection, ErrorCode.InvalidValue);

        var user = await users.GetByUsernameAsync(coordinatorInstanceId, method.Username);
        if (user == null || user.IsSystem)
            return replies.Reject(request, connection);

        if (await authenticator.BeginFactorLoginAsync(user.Id) is not { } challenge)
            return replies.Reject(request, connection, ErrorCode.LoginFactorsMissing);

        return AskSecondStage(request, connection,
            new PendingLogin(challenge, device, request.Device, DeviceLoginMethod.Factors));
    }

    private async Task<HandleMessageResult> LoginWithCodeAsync(LoginRequest request, LoginCodeLoginDTO method,
        IClientConnection connection)
    {
        if (await NewDeviceOf(method.NewDevice) is not { } device)
            return replies.Reject(request, connection, ErrorCode.InvalidValue);

        var userId = await loginCodes.TakeAsync(coordinatorInstanceId, LoginCodes.Hash(method.Code),
            timeProvider.GetUtcNow());
        if (userId == null)
        {
            logger.LogInformation("Refused a login code from {Ip}.", connection.IpEndpoint);
            return replies.Reject(request, connection);
        }

        return Respond(request, connection, await userLogin.AddDeviceAsync(userId.Value, device, request.Device,
            DeviceLoginMethod.LoginCode, connection));
    }

    private async Task<HandleMessageResult> LoginWithLegacyIdentityAsync(LoginRequest request, LegacyLoginDTO method,
        IClientConnection connection)
    {
        if (await NewDeviceOf(method.NewDevice) is not { } device || RecoveryKeys.From(method.Keys) is not { } keys)
            return replies.Reject(request, connection, ErrorCode.InvalidValue);

        var user = await users.GetByIdentityAsync(coordinatorInstanceId, method.UserIdentity);
        if (user == null || user.IsSystem || await users.IsRecoveryKeyTakenAsync(keys.RecoveryKeyHash, user.Id))
            return replies.Reject(request, connection);

        // A session the device still had stands for the second stage it passed then
        var session = method.SessionToken is { } token ? await userLogin.Devices.FindLegacyAsync(token) : null;
        if (session?.UserId != user.Id
            && await authenticator.BeginFactorLoginAsync(user.Id) is { } challenge)
            return AskSecondStage(request, connection,
                new PendingLogin(challenge, device, request.Device, DeviceLoginMethod.Legacy, keys));

        return Respond(request, connection, await LegacyAccounts.MoveOverAsync(userLogin, users, user.Id, keys, device,
            request.Device, connection));
    }

    private HandleMessageResult AskSecondStage(LoginRequest request, IClientConnection connection, PendingLogin login)
    {
        pendingLogins.Start(connection.ConnectionId, login);
        replies.Send(connection.ConnectionId,
            request.ToSecondStageResponse(login.Challenge.Methods.ToList(), ToDto(login.Challenge.Passkey)));
        return HandleMessageResult.NoError;
    }

    /// <summary>The key of the new device, unless it is not a P-256 key or a device has it already.</summary>
    private async Task<NewDevice?> NewDeviceOf(NewDeviceDTO dto) =>
        NewDevice.From(dto) is { } device && !await userLogin.Devices.IsPublicKeyTakenAsync(device.PublicKey)
            ? device
            : null;

    private HandleMessageResult Respond(LoginRequest request, IClientConnection connection, LoginOutcome outcome) =>
        replies.Respond(request, connection, outcome,
            (user, deviceId) => request.ToResponse(LoginReplies.ToDto(user), deviceId));

    public static FIDO2LoginOptionsDTO? ToDto(Abstractions.Security.PasskeyLoginOptions? options) =>
        options == null
            ? null
            : new FIDO2LoginOptionsDTO
            {
                Challenge = options.Challenge,
                RpId = options.RelyingPartyId,
                Timeout = (long)options.TimeoutMs,
                AllowedCredentials = options.AllowedCredentialIds
                    .Select(id => new FIDO2AllowedCredentialDTO { Id = id })
                    .ToList(),
                UserVerification = options.UserVerification
            };
}
