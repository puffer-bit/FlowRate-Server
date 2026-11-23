using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Connections;
using Server.Core.Models.User;
using Shared.Enums.Auth;
using Shared.Models.DataTransferObjects;

namespace Server.Application.Coordinator.Authentication;

/// <summary>Accounts of the earlier protocol 3, which logged in with an identity and session tokens.</summary>
public static class LegacyAccounts
{
    /// <summary>
    /// Moves the account over to devices once its user is let in: it gets the recovery keys, which drop the identity,
    /// its sessions of then end, and the device is added.
    /// </summary>
    public static Task<LoginOutcome> MoveOverAsync(UserLogin userLogin, IUserRepository users, int userId,
        UserRecoveryKeys keys, NewDevice device, DeviceInfoDTO? info, IClientConnection connection) =>
        userLogin.AddDeviceAsync(userId, device, info, DeviceLoginMethod.Legacy, connection, async () =>
        {
            await users.SetRecoveryKeysAsync(userId, keys);
            await userLogin.Devices.EndLegacyAsync(userId);
        });
}
