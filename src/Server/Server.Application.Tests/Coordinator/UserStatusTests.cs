using Server.Application.Mappers;
using Server.Core.Models.User;
using Shared.Enums.User;

namespace Server.Application.Tests.Coordinator;

public class UserStatusTests
{
    [Theory]
    [InlineData(UserStatus.Online, UserStatus.Online)]
    [InlineData(UserStatus.Away, UserStatus.Away)]
    [InlineData(UserStatus.DoNotDisturb, UserStatus.DoNotDisturb)]
    [InlineData(UserStatus.Invisible, UserStatus.Offline)]
    public void ToDto_ConnectedUser_ShowsOthersThePresence(UserStatus status, UserStatus seen)
    {
        var alice = Connected(status);

        Assert.Equal(seen, alice.ToDto().Status);
        Assert.Equal(seen, alice.ToDto(viewerId: 2).Status);
    }

    [Fact]
    public void ToDto_InvisibleUser_SeesTheirOwnStatus()
    {
        var alice = Connected(UserStatus.Invisible);

        Assert.Equal(UserStatus.Invisible, alice.ToDto(viewerId: 1).Status);
    }

    [Fact]
    public void ToDto_UserNotConnected_IsOffline()
    {
        var alice = Connected(UserStatus.Away);
        alice.SetConnected(false);

        Assert.Equal(UserStatus.Offline, alice.ToDto().Status);
    }

    [Fact]
    public void SetStatus_Offline_Throws()
    {
        var alice = Connected(UserStatus.Online);

        Assert.Throws<ArgumentOutOfRangeException>(() => alice.SetStatus(UserStatus.Offline));
    }

    [Fact]
    public void ToDto_ShowsTheIpOnlyToTheUserThemselves_OrWhenAskedFor()
    {
        var alice = UserModel.Create("coordinator", "alice", "203.0.113.7", DateTime.UtcNow);
        alice.Id = 1;

        Assert.Null(alice.ToDto().Ip);
        Assert.Null(alice.ToDto(viewerId: 2).Ip);
        Assert.Equal("203.0.113.7", alice.ToDto(viewerId: 1).Ip);
        Assert.Equal("203.0.113.7", alice.ToDto(viewerId: 2, showIp: true).Ip);
    }

    private static UserModel Connected(UserStatus status)
    {
        var user = UserModel.Create("coordinator", "alice", null, DateTime.UtcNow);
        user.Id = 1;
        user.SetStatus(status);
        user.SetConnected(true);
        return user;
    }
}
