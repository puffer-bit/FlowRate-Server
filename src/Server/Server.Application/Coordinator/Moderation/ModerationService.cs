using Microsoft.Extensions.Logging;
using Server.Application.Mappers;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Roles;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Users;
using Server.Core.Models.Moderation;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models.DataTransferObjects;
using Shared.Models.Notifications.CoordinatorInfo;
using Shared.Models.Requests.Coordinator;
using Shared.Validation;

namespace Server.Application.Coordinator.Moderation;

/// <summary>Active global bans, admission checks and connection effects for one coordinator.</summary>
public sealed class ModerationService(
    string coordinatorId, IBanRepository repository, IUserRepository users, IUserManager userManager,
    IConnectionManager connections, IHybridSessionManager hybridSessions, ICoordinatorPermissions permissions,
    TimeProvider time, ILogger logger) : IDisposable
{
    // Admission and moderation share this lock. A login is either visible to the ban's disconnect pass,
    // or sees the saved ban before it can attach. The gateway also checks before the WebSocket upgrade.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private Task? _expiryTask;
    private Ban[]? _snapshot;
    private DateTimeOffset Now => time.GetUtcNow();

    public void Start() => _expiryTask ??= SweepAsync();

    public async Task<Ban?> CheckAddressAsync(string? address)
    {
        var ip = Ban.NormalizeAddress(address);
        return (await SnapshotAsync()).FirstOrDefault(b => b.Kind == BanKind.Ip && b.Address == ip && b.IsActive(Now));
    }

    private async Task<Ban[]> SnapshotAsync()
    {
        var snapshot = Volatile.Read(ref _snapshot);
        if (snapshot == null)
        {
            var loaded = (await repository.GetAsync(coordinatorId)).ToArray();
            snapshot = Interlocked.CompareExchange(ref _snapshot, loaded, null) ?? loaded;
        }
        return snapshot;
    }

    public async Task<LoginOutcome> AdmitAsync(string? identity, int? userId, IClientConnection connection,
        Func<Task<LoginOutcome>> attach)
    {
        await _gate.WaitAsync();
        try
        {
            var bans = await SnapshotAsync();
            if (bans.Any(b => !b.IsActive(Now)))
                bans = (await LoadAndRestoreAsync()).ToArray();
            if (userId == null && identity != null)
                userId = (await users.GetByIdentityAsync(coordinatorId, identity))?.Id;
            var ip = Ban.NormalizeAddress(connection.IpEndpoint);
            var ban = bans.FirstOrDefault(b => b.IsActive(Now) &&
                (b.Kind == BanKind.Account && b.UserId == userId || b.Kind == BanKind.Ip && b.Address == ip));
            if (ban != null)
                return new LoginOutcome(ErrorCode.Banned, null, null, AttachUserResult.Banned, null, ban.Reason);
            if (userId is { } id) await RestoreAccountAsync(id, bans.Where(b => b.Kind == BanKind.Account && b.UserId == id), Now);
            return await attach();
        }
        finally { _gate.Release(); }
    }

    public async Task<List<BanDTO>> ListAsync()
    {
        await _gate.WaitAsync();
        try { return (await LoadAndRestoreAsync()).Select(b => b.ToDto()).ToList(); }
        finally { _gate.Release(); }
    }

    /// <summary>Freezes or reinstates a user. A banned user stays as they are until the ban is lifted.</summary>
    public async Task<ErrorCode?> SetStateAsync(int actorId, CoordinatorSetUserStateRequest request)
    {
        await _gate.WaitAsync();
        try
        {
            var bans = await LoadAndRestoreAsync();
            var (user, targetError) = await FindTargetAsync(actorId, request.TargetUserId);
            if (user == null) return targetError;
            if (request.State is not (UserState.Normal or UserState.Suspended)) return ErrorCode.InvalidValue;
            // Freezing is separate from banning: neither freezing nor reinstating lifts an account ban
            if (bans.Any(b => b.Kind == BanKind.Account && b.UserId == user.Id && b.IsActive(Now))) return ErrorCode.Conflict;
            var result = await userManager.SetState(user.Id, request.State);
            if (result == null && request.State == UserState.Suspended)
                await hybridSessions.RemoveUserFromAllSessionsAsync(user.Id);
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task<(ErrorCode? Error, Ban? Ban)> BanAsync(int actorId, CoordinatorBanRequest request)
    {
        await _gate.WaitAsync();
        try
        {
            var bans = await LoadAndRestoreAsync();
            var (user, targetError) = await FindTargetAsync(actorId, request.TargetUserId);
            if (user == null) return (targetError, null);

            var reason = request.Reason?.Trim() ?? string.Empty;
            if (reason.Length > Limits.BanReasonMaxLength ||
                request.ExpiresAt is { } expiry && expiry <= Now ||
                request.Kind is not (BanKind.Account or BanKind.Ip)) return (ErrorCode.InvalidValue, null);

            string? address = null;
            if (request.Kind == BanKind.Ip)
            {
                address = Ban.NormalizeAddress(string.IsNullOrWhiteSpace(request.Address) ? user.Ip : request.Address);
                if (address == null) return (ErrorCode.InvalidValue, null);
                // An address may be shared. Do not let an IP action bypass self/hierarchy protection.
                var affected = (await userManager.GetAllUsersAsync()).Values.Where(u =>
                    u.CoordinatorInstanceId == coordinatorId && Ban.NormalizeAddress(u.Ip) == address);
                foreach (var other in affected)
                    if (other.Id == actorId || !await MayTargetAsync(actorId, other.Id))
                        return (ErrorCode.InsufficientPermissions, null);
                foreach (var connection in connections.GetAllConnections().Where(c =>
                    Ban.NormalizeAddress(c.IpEndpoint) == address && c.UserId != null))
                    if (connection.UserId == actorId || !await MayTargetAsync(actorId, connection.UserId!.Value))
                        return (ErrorCode.InsufficientPermissions, null);
            }

            if (bans.Any(b => b.IsActive(Now) && b.Kind == request.Kind &&
                (b.Kind == BanKind.Account ? b.UserId == user.Id : b.Address == address))) return (ErrorCode.Conflict, null);
            var entry = new Ban(Guid.NewGuid(), coordinatorId, request.Kind, user.Id, address, reason,
                actorId, Now, request.ExpiresAt, user.State);
            await repository.AddAsync(entry);
            Volatile.Write(ref _snapshot, bans.Append(entry).ToArray());
            if (request.Kind == BanKind.Account) await userManager.SetState(user.Id, UserState.Banned);
            Disconnect(entry);
            logger.LogInformation("Ban {BanId} ({Kind}) created by {ActorId} for user {UserId}.",
                entry.Id, entry.Kind, actorId, user.Id);
            return (null, entry);
        }
        finally { _gate.Release(); }
    }

    /// <summary>The user an action is for, or why the actor may not act on them.</summary>
    private async Task<(UserModel? User, ErrorCode? Error)> FindTargetAsync(int actorId, int targetUserId)
    {
        var user = await userManager.GetUserAsync(targetUserId);
        if (user == null || user.CoordinatorInstanceId != coordinatorId) return (null, ErrorCode.UserNotFound);
        if (user.IsSystem || user.Id == actorId) return (null, ErrorCode.CannotTargetSelf);
        if (!await MayTargetAsync(actorId, user.Id)) return (null, ErrorCode.InsufficientPermissions);
        return (user, null);
    }

    public async Task<ErrorCode?> RevokeAsync(int actorId, Guid id)
    {
        await _gate.WaitAsync();
        try
        {
            var bans = await repository.GetAsync(coordinatorId);
            var ban = bans.FirstOrDefault(b => b.Id == id);
            if (ban == null) return ErrorCode.BanNotFound;
            if (ban.UserId is { } userId && !await MayTargetAsync(actorId, userId)) return ErrorCode.InsufficientPermissions;
            await RemoveAsync(ban, bans);
            await LoadAndRestoreAsync();
            logger.LogInformation("Ban {BanId} revoked by {ActorId}.", id, actorId);
            return null;
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> MayTargetAsync(int actor, int target) => actor == UserModel.SystemUserId ||
        actor != target && permissions.Has(actor, Permission.BanMembers) &&
        await permissions.HighestPositionOfAsync(actor) > await permissions.HighestPositionOfAsync(target);

    private void Disconnect(Ban ban)
    {
        foreach (var connection in connections.GetAllConnections())
        {
            if (!(ban.Kind == BanKind.Account && connection.UserId == ban.UserId ||
                ban.Kind == BanKind.Ip && Ban.NormalizeAddress(connection.IpEndpoint) == ban.Address)) continue;
            try { connection.Send(new CoordinatorUserBannedNotification(connection.UserId ?? 0, ban.Reason)); }
            catch (Exception ex) { logger.LogWarning(ex, "Sending ban notice to {ConnectionId} failed.", connection.ConnectionId); }
            try { connection.Close(); }
            catch (Exception ex) { logger.LogWarning(ex, "Closing banned connection {ConnectionId} failed.", connection.ConnectionId); }
        }
    }

    private async Task<List<Ban>> LoadAndRestoreAsync()
    {
        var bans = await repository.GetAsync(coordinatorId);
        var now = Now;
        foreach (var group in bans.Where(b => b.Kind == BanKind.Account && b.UserId != null).GroupBy(b => b.UserId!.Value))
            await RestoreAccountAsync(group.Key, group, now);
        foreach (var ban in bans.Where(b => !b.IsActive(now)).ToList())
        {
            await repository.DeleteAsync(coordinatorId, ban.Id);
            bans.Remove(ban);
        }
        Volatile.Write(ref _snapshot, bans.ToArray());
        return bans;
    }

    private async Task RemoveAsync(Ban ban, List<Ban> bans)
    {
        // Restore before deleting so a failed state update can be retried after a restart.
        var now = Now;
        if (ban.Kind == BanKind.Account && ban.UserId is { } userId)
            await RestoreAccountAsync(userId, bans.Select(b => b.Id == ban.Id ? b with { ExpiresAt = now } : b), now);
        await repository.DeleteAsync(coordinatorId, ban.Id);
        bans.Remove(ban);
    }

    private async Task RestoreAccountAsync(int userId, IEnumerable<Ban> entries, DateTimeOffset now)
    {
        var bans = entries.ToList();
        if (bans.Count == 0) return;
        var user = await userManager.GetUserAsync(userId);
        if (user == null) return;
        if (bans.Any(b => b.IsActive(now)))
        {
            if (!user.IsBanned) await RestoreStateAsync(user.Id, UserState.Banned);
        }
        else if (user.IsBanned)
        {
            var previous = bans.OrderByDescending(b => b.CreatedAt).First().PreviousState;
            await RestoreStateAsync(user.Id, previous == UserState.Suspended ? previous : UserState.Normal);
        }
    }

    private async Task RestoreStateAsync(int userId, UserState state)
    {
        if (await userManager.SetState(userId, state) is { } error)
            throw new InvalidOperationException($"Restoring ban state for user {userId} failed: {error}.");
    }

    private async Task SweepAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), time);
        try
        {
            do
            {
                try
                {
                    await _gate.WaitAsync(_stopping.Token);
                    try { await LoadAndRestoreAsync(); }
                    finally { _gate.Release(); }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { logger.LogError(ex, "Refreshing bans failed."); }
            } while (await timer.WaitForNextTickAsync(_stopping.Token));
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
    }

    public void Dispose() => _stopping.Cancel();
}
