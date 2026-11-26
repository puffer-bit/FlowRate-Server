using Server.Core.Models.User;
using Shared.Enums.User;

namespace Server.Core.Tests.Models;

public class UserIdlePresenceTests
{
    private static UserModel ConnectedUser(UserStatus status, params string[] connectionIds)
    {
        var user = new UserModel(1, "coordinator", "user", UserState.Normal, status,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        user.SetConnected(true);
        foreach (var connectionId in connectionIds)
            user.AddDevice(connectionId, null);
        return user;
    }

    [Fact]
    public void OnlineUser_IsSeenIdle_WhenTheOnlyDeviceIsIdle()
    {
        var user = ConnectedUser(UserStatus.Online, "pc");

        Assert.True(user.SetIdleLevel("pc", IdleLevel.Idle));

        Assert.Equal(UserStatus.Idle, user.Presence);
        Assert.Equal(UserStatus.Idle, user.OwnPresence);
        Assert.Equal(UserStatus.Online, user.Status);
    }

    [Fact]
    public void InvisibleUser_SeesThemselvesInvisible_WhileOthersSeeThemOffline()
    {
        var user = ConnectedUser(UserStatus.Invisible, "pc");
        user.SetIdleLevel("pc", IdleLevel.Sleeping);

        Assert.Equal(UserStatus.Invisible, user.OwnPresence);
        Assert.Equal(UserStatus.Offline, user.Presence);
    }

    [Fact]
    public void OneActiveDevice_KeepsTheUserOnline()
    {
        var user = ConnectedUser(UserStatus.Online, "pc", "laptop");

        Assert.False(user.SetIdleLevel("pc", IdleLevel.Sleeping));

        Assert.Equal(UserStatus.Online, user.Presence);
    }

    [Fact]
    public void UserIsSeenAtTheLeastIdleLevelOfTheDevices()
    {
        var user = ConnectedUser(UserStatus.Online, "pc", "laptop");

        user.SetIdleLevel("pc", IdleLevel.Sleeping);
        user.SetIdleLevel("laptop", IdleLevel.Idle);

        Assert.Equal(UserStatus.Idle, user.Presence);
    }

    [Fact]
    public void ClosingTheActiveDevice_LeavesTheIdleOneSeen()
    {
        var user = ConnectedUser(UserStatus.Online, "pc", "phone");
        user.SetIdleLevel("pc", IdleLevel.Sleeping);

        Assert.True(user.RemoveDevice("phone"));

        Assert.Equal(UserStatus.Sleeping, user.Presence);
    }

    [Fact]
    public void NewConnection_StartsActive()
    {
        var user = ConnectedUser(UserStatus.Online, "pc");
        user.SetIdleLevel("pc", IdleLevel.Idle);

        Assert.True(user.AddDevice("phone", null));

        Assert.Equal(UserStatus.Online, user.Presence);
    }

    [Theory]
    [InlineData(UserStatus.Away, UserStatus.Away)]
    [InlineData(UserStatus.DoNotDisturb, UserStatus.DoNotDisturb)]
    [InlineData(UserStatus.Invisible, UserStatus.Offline)]
    public void PickedStatusOtherThanOnline_IsShownAsItIs(UserStatus picked, UserStatus seen)
    {
        var user = ConnectedUser(picked, "pc");

        Assert.False(user.SetIdleLevel("pc", IdleLevel.Sleeping));

        Assert.Equal(seen, user.Presence);
    }

    [Fact]
    public void Reconnecting_ForgetsTheIdleLevels()
    {
        var user = ConnectedUser(UserStatus.Online, "pc");
        user.SetIdleLevel("pc", IdleLevel.Sleeping);

        user.SetConnected(false);
        user.SetConnected(true);
        user.AddDevice("pc2", null);

        Assert.Equal(UserStatus.Online, user.Presence);
    }

    [Fact]
    public void ConnectionThatIsNotOpen_IsIgnored()
    {
        var user = ConnectedUser(UserStatus.Online, "pc");

        Assert.False(user.SetIdleLevel("gone", IdleLevel.Idle));

        Assert.Equal(UserStatus.Online, user.Presence);
    }

    [Theory]
    [InlineData(UserStatus.Offline)]
    [InlineData(UserStatus.Idle)]
    [InlineData(UserStatus.Sleeping)]
    public void ShownOnlyStatuses_CannotBePicked(UserStatus status)
    {
        Assert.False(UserModel.CanPick(status));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConnectedUser(UserStatus.Online).SetStatus(status));
    }
}
