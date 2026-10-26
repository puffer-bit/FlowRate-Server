using Shared.Enums.User;

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

    /// <summary>The status others see: offline while the user is not connected or invisible.</summary>
    public UserStatus Presence => IsConnected && Status != UserStatus.Invisible ? Status : UserStatus.Offline;

    public DateTime FirstConnectionTime { get; }
    public DateTime LastConnectionTime { get; private set; }

    /// <summary>The coordinator roles of the user, without the everyone role.</summary>
    public IReadOnlyCollection<int> RoleIds { get; private set; } = [];

    public bool IsSystem => State == UserState.System;
    public bool IsBanned => State == UserState.Banned;

    public UserModel(int id, string coordinatorInstanceId, string username, UserState state, UserStatus status,
        DateTime firstConnectionTime, DateTime lastConnectionTime,
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
    public static UserModel Create(string coordinatorInstanceId, string username, string? ip, DateTime now) =>
        new(0, coordinatorInstanceId, username, UserState.Normal, UserStatus.Online, now, now, ip);

    /// <summary>The in-process user the server itself acts as. Never saved.</summary>
    public static UserModel CreateSystem(string coordinatorInstanceId, string? ip, DateTime now) =>
        new(SystemUserId, coordinatorInstanceId, "System", UserState.System, UserStatus.Online, now, now, ip);

    public void MarkConnected(string? ip, DateTime now)
    {
        Ip = ip;
        LastConnectionTime = now;
    }

    public void SetConnected(bool connected) => IsConnected = connected;

    public void SetStatus(UserStatus status)
    {
        if (status == UserStatus.Offline || !Enum.IsDefined(status))
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
