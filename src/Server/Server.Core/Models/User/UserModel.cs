using Shared.Enums.User;
using Shared.Models.DataTransferObjects;

namespace Server.Core.Models.User;

public sealed class UserModel
{
    public const int SystemUserId = -1;

    // Assigned by the database after the first save
    public int Id { get; set; }
    public string CoordinatorInstanceId { get; }
    public string Username { get; private set; }
    public string? Ip { get; private set; }
    public string? RegionName { get; private set; }
    public string? RegionCode { get; private set; }
    public string? DisplayName { get; private set; }
    public string? Prefix { get; private set; }
    public string? AvatarUrl { get; private set; }
    public UserState State { get; private set; }

    /// <summary>The status the user picked; never <see cref="UserStatus.Offline"/>.</summary>
    public UserStatus Status { get; private set; }

    /// <summary>Whether the user is connected now. Not saved: a user read from the database is not.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// The status others see: offline while the user is not connected or invisible; idle or sleeping instead of
    /// online while every open connection reports that level.
    /// </summary>
    public UserStatus Presence => !IsConnected || Status == UserStatus.Invisible
        ? UserStatus.Offline
        : Status != UserStatus.Online
            ? Status
            : IdleLevel switch
            {
                IdleLevel.Sleeping => UserStatus.Sleeping,
                IdleLevel.Idle => UserStatus.Idle,
                _ => UserStatus.Online
            };

    /// <summary>The least idle level among the open connections; active while none is open. Not saved.</summary>
    public IdleLevel IdleLevel
    {
        get
        {
            lock (_devices)
                return _idleLevels.Count == 0 ? IdleLevel.Active : _idleLevels.Values.Min();
        }
    }

    /// <summary>
    /// The device others see, among the connections open now: a phone first, since one is connected only while it is
    /// used, unlike a computer left on; otherwise the device connected last. Null while not connected. Not saved.
    /// </summary>
    public UserDevice? Device
    {
        get
        {
            lock (_devices)
            {
                for (var i = _devices.Count - 1; i >= 0; i--)
                {
                    if (DevicePlatforms.IsMobile(_devices[i].Device.Platform))
                        return _devices[i].Device;
                }
                return _devices.Count == 0 ? null : _devices[^1].Device;
            }
        }
    }

    // The devices of the open connections, the last connected at the end
    private readonly List<(string ConnectionId, UserDevice Device)> _devices = [];

    // The idle level of every open connection, guarded by the lock of _devices
    private readonly Dictionary<string, IdleLevel> _idleLevels = [];

    public DateTimeOffset FirstConnectionTime { get; }
    public DateTimeOffset LastConnectionTime { get; private set; }

    /// <summary>The coordinator roles of the user, without the everyone role.</summary>
    public IReadOnlyCollection<int> RoleIds { get; private set; } = [];

    public bool IsSystem => State == UserState.System;
    public bool IsBanned => State == UserState.Banned;

    public UserModel(int id, string coordinatorInstanceId, string username, UserState state, UserStatus status,
        DateTimeOffset firstConnectionTime, DateTimeOffset lastConnectionTime,
        string? ip = null, string? regionName = null, string? regionCode = null,
        string? displayName = null, string? prefix = null, string? avatarUrl = null,
        IEnumerable<int>? roleIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinatorInstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        Id = id;
        CoordinatorInstanceId = coordinatorInstanceId;
        Username = username;
        State = state;
        // Saved before the user picked one
        Status = status == UserStatus.Offline ? UserStatus.Online : status;
        FirstConnectionTime = firstConnectionTime;
        LastConnectionTime = lastConnectionTime;
        Ip = ip;
        RegionName = regionName;
        RegionCode = regionCode;
        DisplayName = displayName;
        Prefix = prefix;
        AvatarUrl = avatarUrl;
        RoleIds = roleIds?.ToHashSet() ?? [];
    }

    /// <summary>A new user connecting for the first time; not saved yet.</summary>
    public static UserModel Create(string coordinatorInstanceId, string username, string? ip, DateTimeOffset now) =>
        new(0, coordinatorInstanceId, username, UserState.Normal, UserStatus.Online, now, now, ip);

    /// <summary>The in-process user the server itself acts as. Never saved.</summary>
    public static UserModel CreateSystem(string coordinatorInstanceId, string? ip, DateTimeOffset now) =>
        new(SystemUserId, coordinatorInstanceId, "System", UserState.System, UserStatus.Online, now, now, ip);

    public void MarkConnected(string? ip, DateTimeOffset now)
    {
        Ip = ip;
        LastConnectionTime = now;
    }

    public void SetConnected(bool connected)
    {
        IsConnected = connected;
        if (!connected)
            lock (_devices)
            {
                _devices.Clear();
                _idleLevels.Clear();
            }
    }

    /// <summary>
    /// The connection the user connected on has this device and starts active; true when <see cref="Device"/> or
    /// <see cref="Presence"/> changed.
    /// </summary>
    public bool AddDevice(string connectionId, UserDevice? device)
    {
        lock (_devices)
        {
            var before = (Device, Presence);
            _devices.RemoveAll(entry => entry.ConnectionId == connectionId);
            if (device != null)
                _devices.Add((connectionId, device));
            _idleLevels[connectionId] = IdleLevel.Active;
            return before != (Device, Presence);
        }
    }

    /// <summary>The connection closed; true when <see cref="Device"/> or <see cref="Presence"/> changed.</summary>
    public bool RemoveDevice(string connectionId)
    {
        lock (_devices)
        {
            var before = (Device, Presence);
            _devices.RemoveAll(entry => entry.ConnectionId == connectionId);
            _idleLevels.Remove(connectionId);
            return before != (Device, Presence);
        }
    }

    /// <summary>
    /// An open connection reports how long its device has gone without input; true when <see cref="Presence"/>
    /// changed. A connection that is not open is ignored.
    /// </summary>
    public bool SetIdleLevel(string connectionId, IdleLevel level)
    {
        if (!Enum.IsDefined(level))
            throw new ArgumentOutOfRangeException(nameof(level), level, null);

        lock (_devices)
        {
            if (!_idleLevels.ContainsKey(connectionId))
                return false;

            var before = Presence;
            _idleLevels[connectionId] = level;
            return before != Presence;
        }
    }

    /// <summary>Whether a user may pick the status; offline, idle and sleeping are only shown.</summary>
    public static bool CanPick(UserStatus status) =>
        status is UserStatus.Online or UserStatus.Away or UserStatus.DoNotDisturb or UserStatus.Invisible;

    public void SetStatus(UserStatus status)
    {
        if (!CanPick(status))
            throw new ArgumentOutOfRangeException(nameof(status), status, "A user cannot pick this status.");
        Status = status;
    }

    public void Rename(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        Username = username;
    }

    public void SetAvatar(string? avatarUrl) => AvatarUrl = avatarUrl;

    public void SetRoles(IEnumerable<int> roleIds)
    {
        if (IsSystem)
            throw new InvalidOperationException("The system user has no roles.");
        RoleIds = roleIds.ToHashSet();
    }

    public void UpdateProfile(string? displayName, string? prefix, string? avatarUrl)
    {
        DisplayName = displayName;
        Prefix = prefix;
        AvatarUrl = avatarUrl;
    }

    public void SetRegion(string? regionName, string? regionCode)
    {
        RegionName = regionName;
        RegionCode = regionCode;
    }

    public void Ban() => ChangeState(UserState.Banned);

    public void Suspend() => ChangeState(UserState.Suspended);

    public void Reinstate() => ChangeState(UserState.Normal);

    private void ChangeState(UserState state)
    {
        if (IsSystem)
            throw new InvalidOperationException("The system user cannot change state.");
        State = state;
    }
}
