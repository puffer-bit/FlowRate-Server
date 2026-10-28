using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Connections;
using Server.Core.Models.User;
using Shared.Enums.User;
using Shared.Models.DataTransferObjects;
using Shared.Models.Notifications.CoordinatorInfo;

namespace Server.Application.Coordinator.Authentication;

/// <summary>
/// The devices users are logged in from. A login with the identity starts a session; the device then logs in with the
/// session token, which changes on every login, until the session goes unused for <see cref="Lifetime"/> or is ended.
/// </summary>
public sealed class UserSessions(
    IUserSessionRepository repository,
    IConnectionManager connectionManager,
    string coordinatorInstanceId,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    private const string UnknownDevice = "Unknown";

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public Task<UserSession?> FindAsync(string token) =>
        repository.FindByTokenAsync(coordinatorInstanceId, SecretHash.Of(token), Now);

    /// <summary>Starts a session for a device that logged in with the identity; returns its token.</summary>
    public async Task<string> StartAsync(int userId, DeviceInfoDTO? device, IClientConnection connection)
    {
        var token = SecretHash.CreateToken();
        var now = Now;
        var session = new UserSession(Guid.NewGuid(), coordinatorInstanceId, userId, SecretHash.Of(token), null,
            device?.Name ?? UnknownDevice, device?.Platform ?? string.Empty, device?.AppVersion ?? string.Empty,
            now, now, now + Lifetime, connection.IpEndpoint);

        await repository.DeleteExpiredAsync(now);
        await repository.AddAsync(session);
        connection.UserSessionId = session.Id;
        return token;
    }

    /// <summary>Lets the device of the session in again with the token it sent; returns the next token.</summary>
    public async Task<string> ResumeAsync(UserSession session, string presentedToken, DeviceInfoDTO? device,
        IClientConnection connection)
    {
        var token = SecretHash.CreateToken();
        var now = Now;
        await repository.UpdateAsync(session with
        {
            TokenHash = SecretHash.Of(token),
            PreviousTokenHash = SecretHash.Of(presentedToken),
            DeviceName = device?.Name ?? session.DeviceName,
            DevicePlatform = device?.Platform ?? session.DevicePlatform,
            AppVersion = device?.AppVersion ?? session.AppVersion,
            LastUsedAt = now,
            ExpiresAt = now + Lifetime,
            LastAddress = connection.IpEndpoint
        });
        connection.UserSessionId = session.Id;
        return token;
    }

    public async Task<List<UserSessionDTO>> GetAsync(int userId, Guid? currentSessionId)
    {
        var online = connectionManager.GetUserConnections(userId)
            .Select(connection => connection.UserSessionId)
            .ToHashSet();
        var sessions = await repository.GetByUserAsync(coordinatorInstanceId, userId, Now);
        return sessions
            .Select(session => new UserSessionDTO(session.Id,
                new DeviceInfoDTO(session.DeviceName, session.DevicePlatform, session.AppVersion),
                session.CreatedAt, session.LastUsedAt, session.ExpiresAt, session.LastAddress,
                session.Id == currentSessionId, online.Contains(session.Id)))
            .ToList();
    }

    /// <summary>Ends one session of the user and disconnects its device; false when the user has no such session.</summary>
    public async Task<bool> EndAsync(int userId, Guid sessionId)
    {
        if (!await repository.DeleteAsync(coordinatorInstanceId, userId, sessionId))
            return false;

        Disconnect(userId, new HashSet<Guid> { sessionId }, UserSessionEndReason.Revoked);
        return true;
    }

    /// <summary>Ends every session of the user but <paramref name="keep"/> and disconnects their devices.</summary>
    public async Task EndAllButAsync(int userId, Guid? keep, UserSessionEndReason reason)
    {
        var ended = await repository.DeleteAllButAsync(coordinatorInstanceId, userId, keep);
        Disconnect(userId, ended.ToHashSet(), reason);
    }

    private void Disconnect(int userId, IReadOnlySet<Guid> sessionIds, UserSessionEndReason reason)
    {
        foreach (var connection in connectionManager.GetUserConnections(userId))
        {
            if (connection.UserSessionId is not { } id || !sessionIds.Contains(id))
                continue;

            connection.Send(new CoordinatorUserSessionEndedNotification(reason));
            connection.Close();
        }
    }
}
