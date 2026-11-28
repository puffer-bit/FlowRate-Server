using Microsoft.Extensions.Logging;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Connections;
using Server.Core.Models.User;
using Shared.Enums.Auth;
using Shared.Models.DataTransferObjects;
using Shared.Models.Notifications.CoordinatorInfo;

namespace Server.Application.Coordinator.Authentication;

/// <summary>
/// The devices of users' accounts. A device logs in by signing a challenge with its key, until it goes unused for its
/// lifetime (<see cref="Lifetime"/>, <see cref="TemporaryLifetime"/> for temporary ones) or is removed. Unused means
/// offline: a login and every hour online (<see cref="KeepAliveInterval"/>) and the disconnect start the lifetime anew,
/// so a bot that stays connected for months keeps its device.
/// </summary>
public sealed class UserSessions(
    IUserSessionRepository repository,
    IConnectionManager connectionManager,
    string coordinatorInstanceId,
    TimeProvider timeProvider,
    ILogger? logger = null) : IDisposable
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);
    public static readonly TimeSpan TemporaryLifetime = TimeSpan.FromDays(1);

    /// <summary>How often the devices online live on; well under the shortest lifetime.</summary>
    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromHours(1);

    private const string UnknownDevice = "Unknown";

    private readonly CancellationTokenSource _stopping = new();
    private Task? _keepAliveTask;

    private DateTimeOffset Now => timeProvider.GetUtcNow();

    /// <summary>A device with a key, unless it has expired.</summary>
    public async Task<UserSession?> GetAsync(Guid deviceId) =>
        await repository.GetAsync(coordinatorInstanceId, deviceId, Now) is { PublicKey: not null } device ? device : null;

    /// <summary>The session of the earlier protocol 3 the token belongs to, unless it has expired.</summary>
    public Task<UserSession?> FindLegacyAsync(string token) =>
        repository.FindByTokenAsync(coordinatorInstanceId, SecretHash.Of(token), Now);

    public Task<bool> IsPublicKeyTakenAsync(byte[] publicKey) => repository.IsPublicKeyTakenAsync(publicKey);

    /// <summary>
    /// Adds a device to the account of the user the connection is attached to, and tells the user's other devices.
    /// </summary>
    public async Task<UserSession> AddAsync(int userId, NewDevice device, DeviceInfoDTO? info,
        DeviceLoginMethod method, IClientConnection connection)
    {
        var now = Now;
        var session = new UserSession(Guid.NewGuid(), coordinatorInstanceId, userId, null, null,
            info?.Name ?? UnknownDevice, info?.Platform ?? string.Empty, info?.AppVersion ?? string.Empty,
            now, now, now + LifetimeOf(device.IsTemporary), connection.IpEndpoint, device.PublicKey,
            device.IsTemporary, method);

        await repository.DeleteExpiredAsync(now);
        await repository.AddAsync(session);
        connection.UserSessionId = session.Id;

        var added = new AccountDeviceAddedNotification(ToDto(session, null, isOnline: true));
        foreach (var other in connectionManager.GetUserConnections(userId))
        {
            if (other.ConnectionId != connection.ConnectionId)
                other.Send(added);
        }

        return session;
    }

    /// <summary>The device logged in again: it lives on for another lifetime.</summary>
    public async Task ResumeAsync(UserSession device, DeviceInfoDTO? info, IClientConnection connection)
    {
        var now = Now;
        await repository.UpdateAsync(device with
        {
            DeviceName = info?.Name ?? device.DeviceName,
            DevicePlatform = info?.Platform ?? device.DevicePlatform,
            AppVersion = info?.AppVersion ?? device.AppVersion,
            LastUsedAt = now,
            ExpiresAt = now + LifetimeOf(device.IsTemporary),
            LastAddress = connection.IpEndpoint
        });
        connection.UserSessionId = device.Id;
    }

    public async Task<List<AccountDeviceDTO>> GetAsync(int userId, Guid? currentDeviceId)
    {
        var online = connectionManager.GetUserConnections(userId)
            .Select(connection => connection.UserSessionId)
            .ToHashSet();
        var devices = await repository.GetByUserAsync(coordinatorInstanceId, userId, Now);
        return devices
            .Select(device => ToDto(device, currentDeviceId, online.Contains(device.Id)))
            .ToList();
    }

    /// <summary>Removes one device of the user and disconnects it; false when the user has no such device.</summary>
    public async Task<bool> EndAsync(int userId, Guid deviceId, DeviceEndReason reason = DeviceEndReason.Revoked)
    {
        if (!await repository.DeleteAsync(coordinatorInstanceId, userId, deviceId))
            return false;

        Disconnect(userId, new HashSet<Guid> { deviceId }, reason);
        return true;
    }

    /// <summary>Removes every device of the user but <paramref name="keep"/> and disconnects them.</summary>
    public async Task EndAllButAsync(int userId, Guid? keep, DeviceEndReason reason = DeviceEndReason.Revoked)
    {
        var ended = await repository.DeleteAllButAsync(coordinatorInstanceId, userId, keep);
        Disconnect(userId, ended.ToHashSet(), reason);
    }

    /// <summary>Removes the sessions of the earlier protocol 3 of a user whose account moved over to devices.</summary>
    public Task EndLegacyAsync(int userId) => repository.DeleteLegacyAsync(coordinatorInstanceId, userId);

    /// <summary>Starts giving the devices online another lifetime every <see cref="KeepAliveInterval"/>.</summary>
    public void Start() => _keepAliveTask ??= KeepAliveLoopAsync();

    /// <summary>The devices with a connection now live on for another lifetime from now.</summary>
    public Task KeepOnlineAliveAsync() => KeepAliveAsync(connectionManager.GetAllConnections()
        .Select(connection => connection.UserSessionId)
        .OfType<Guid>()
        .Distinct()
        .ToList());

    /// <summary>
    /// The devices live on for another lifetime from now; expired and removed ones stay so. The last login stays too.
    /// </summary>
    public Task KeepAliveAsync(IReadOnlyCollection<Guid> deviceIds)
    {
        if (deviceIds.Count == 0)
            return Task.CompletedTask;

        var now = Now;
        return repository.ExtendAsync(coordinatorInstanceId, deviceIds, now, now + Lifetime, now + TemporaryLifetime);
    }

    private async Task KeepAliveLoopAsync()
    {
        using var timer = new PeriodicTimer(KeepAliveInterval, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(_stopping.Token))
            {
                try
                {
                    await KeepOnlineAliveAsync();
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Keeping the devices online alive failed.");
                }
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
    }

    public void Dispose() => _stopping.Cancel();

    private static TimeSpan LifetimeOf(bool isTemporary) => isTemporary ? TemporaryLifetime : Lifetime;

    private static AccountDeviceDTO ToDto(UserSession device, Guid? currentDeviceId, bool isOnline) =>
        new(device.Id, new DeviceInfoDTO(device.DeviceName, device.DevicePlatform, device.AppVersion),
            device.CreatedAt, device.LastUsedAt, device.ExpiresAt, device.LastAddress, device.Id == currentDeviceId,
            isOnline, device.IsTemporary, device.AddedBy);

    private void Disconnect(int userId, IReadOnlySet<Guid> deviceIds, DeviceEndReason reason)
    {
        foreach (var connection in connectionManager.GetUserConnections(userId))
        {
            if (connection.UserSessionId is not { } id || !deviceIds.Contains(id))
                continue;

            connection.Send(new AccountDeviceEndedNotification(reason));
            connection.Close();
        }
    }
}
