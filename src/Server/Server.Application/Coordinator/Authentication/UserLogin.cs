using Shared.Enums;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Users;
using Server.Core.Models.User;
using Shared.Enums.Auth;
using Shared.Models.DataTransferObjects;

namespace Server.Application.Coordinator.Authentication;

/// <summary>
/// The last step of every login: bans are checked, the user is attached, the connection is theirs and the device is
/// added or lives on. The other connections of the user stay: a user can be connected from several devices.
/// </summary>
public sealed class UserLogin(IUserManager userManager, UserSessions devices,
    Server.Application.Coordinator.Moderation.ModerationService? moderation = null)
{
    public UserSessions Devices => devices;

    /// <summary>A device of the account that signed the challenge.</summary>
    public Task<LoginOutcome> ResumeAsync(UserSession device, DeviceInfoDTO? info, IClientConnection connection) =>
        AdmitAsync(device.UserId, connection, async () =>
        {
            connection.Device = ToDevice(info);
            var attachment = await userManager.AttachUser(device.UserId, connection);
            if (Rejected(attachment) is { } rejected)
                return rejected;

            await devices.ResumeAsync(device, info, connection);
            return new LoginOutcome(null, attachment.User, device.Id);
        });

    /// <summary>
    /// A user who proved themselves another way gets a new device. <paramref name="onAttached"/> runs once the user is
    /// let in, before the device is added.
    /// </summary>
    public Task<LoginOutcome> AddDeviceAsync(int userId, NewDevice device, DeviceInfoDTO? info,
        DeviceLoginMethod method, IClientConnection connection, Func<Task>? onAttached = null) =>
        AdmitAsync(userId, connection, async () =>
        {
            connection.Device = ToDevice(info);
            var attachment = await userManager.AttachUser(userId, connection);
            if (Rejected(attachment) is { } rejected)
                return rejected;

            if (onAttached != null)
                await onAttached();
            var added = await devices.AddAsync(userId, device, info, method, connection);
            return new LoginOutcome(null, attachment.User, added.Id);
        });

    /// <summary>A new account, with the device that made it.</summary>
    public Task<LoginOutcome> RegisterAsync(UserRecoveryKeys recoveryKeys, NewDevice device, DeviceInfoDTO? info,
        IClientConnection connection) =>
        AdmitAsync(null, connection, async () =>
        {
            connection.Device = ToDevice(info);
            var attachment = await userManager.RegisterUser(recoveryKeys, connection);
            if (Rejected(attachment) is { } rejected)
                return rejected;

            var user = attachment.User!;
            var added = await devices.AddAsync(user.Id, device, info, DeviceLoginMethod.Registration, connection);
            return new LoginOutcome(null, user, added.Id);
        });

    private Task<LoginOutcome> AdmitAsync(int? userId, IClientConnection connection, Func<Task<LoginOutcome>> attach) =>
        moderation == null ? attach() : moderation.AdmitAsync(userId, connection, attach);

    private static UserDevice? ToDevice(DeviceInfoDTO? device) =>
        device == null ? null : new UserDevice(device.Platform, device.OsVersion, device.Architecture, device.AppVersion);

    private static LoginOutcome? Rejected(UserAttachment attachment) =>
        attachment is { Result: AttachUserResult.Attached, User: not null }
            ? null
            : LoginOutcome.Refused(attachment.Result switch
            {
                AttachUserResult.OnlineLimitReached => ErrorCode.OnlineLimitReached,
                AttachUserResult.UserLimitReached => ErrorCode.UserLimitReached,
                AttachUserResult.ConnectionLimitReached => ErrorCode.RateLimited,
                AttachUserResult.Banned => ErrorCode.Banned,
                _ => ErrorCode.AuthFailed
            });
}

/// <param name="Error">Why the user was not let in; null when it was.</param>
/// <param name="DeviceId">The device the client logs in with from now on.</param>
public sealed record LoginOutcome(ErrorCode? Error, UserModel? User, Guid? DeviceId, string? BanReason = null)
{
    public static LoginOutcome Refused(ErrorCode error, string? banReason = null) => new(error, null, null, banReason);
}
