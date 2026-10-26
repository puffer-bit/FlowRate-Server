using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Users;
using Server.Core.Models.Coordinator;
using Server.Core.Models.Coordinator.Settings;
using Server.Core.Models.User;

namespace Server.Application.Tests.Coordinator;

public class UserManagerLimitsTests
{
    private const string CoordinatorId = "coordinator";

    private readonly Mock<IUserRepository> _users = new();
    private readonly UserManagerContext _context = new();
    private readonly CoordinatorInstanceContext _coordinator = new(Model(maxOnlineUsers: 2, maxUsers: 10), "1.0");
    private readonly IClientConnection _connection = Mock.Of<IClientConnection>(c => c.IpEndpoint == "127.0.0.1");
    private readonly UserManager _manager;
    private int _nextId = 100;

    public UserManagerLimitsTests()
    {
        _users.Setup(r => r.AddAsync(It.IsAny<UserModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _nextId++);
        _users.Setup(r => r.GetUsernamesStartingWithAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());
        _users.Setup(r => r.CountAsync(CoordinatorId, It.IsAny<CancellationToken>())).ReturnsAsync(5);

        _manager = new UserManager(_context, NullLogger.Instance, CoordinatorId, _users.Object, _coordinator);
    }

    [Fact]
    public async Task Attach_BelowTheLimits_Attaches()
    {
        var attachment = await _manager.AttachUser(null, _connection);

        Assert.Equal(AttachUserResult.Attached, attachment.Result);
        Assert.NotNull(attachment.User);
        Assert.NotNull(attachment.UserIdentity);
    }

    [Fact]
    public async Task Attach_WhenAsManyAreOnlineAsAllowed_IsRejectedWithoutCreatingAUser()
    {
        Online(1, 2);

        var attachment = await _manager.AttachUser(null, _connection);

        Assert.Equal(AttachUserResult.OnlineLimitReached, attachment.Result);
        Assert.Equal(2, _manager.ConnectedUsersCount);
        _users.Verify(r => r.AddAsync(It.IsAny<UserModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Attach_OfAUserAlreadyOnline_TakesTheirOwnPlace()
    {
        Online(1, 2);
        var returning = User(2);
        _users.Setup(r => r.GetByIdentityAsync(It.IsAny<string>(), "#returning", It.IsAny<CancellationToken>())).ReturnsAsync(returning);

        // The first check sees a full server, the user is let in only once known to be online already
        var attachment = await _manager.AttachUser("#returning", _connection);

        Assert.Equal(AttachUserResult.OnlineLimitReached, attachment.Result);
    }

    [Fact]
    public async Task Attach_SystemUser_IgnoresTheOnlineLimit()
    {
        Online(1, 2);

        var attachment = await _manager.AttachUser("system", _connection);

        Assert.Equal(AttachUserResult.Attached, attachment.Result);
    }

    [Fact]
    public async Task ConnectedUsersCount_LeavesOutTheSystemUser()
    {
        Online(1);

        await _manager.AttachUser("system", _connection);

        Assert.Equal(1, _manager.ConnectedUsersCount);
    }

    [Fact]
    public async Task Attach_NewUser_WhenAsManyAreRegisteredAsAllowed_IsRejected()
    {
        _users.Setup(r => r.CountAsync(CoordinatorId, It.IsAny<CancellationToken>())).ReturnsAsync(10);

        var attachment = await _manager.AttachUser(null, _connection);

        Assert.Equal(AttachUserResult.UserLimitReached, attachment.Result);
        Assert.Equal(0, _manager.ConnectedUsersCount);
    }

    [Fact]
    public async Task Attach_KnownUser_WhenAsManyAreRegisteredAsAllowed_Attaches()
    {
        _users.Setup(r => r.CountAsync(CoordinatorId, It.IsAny<CancellationToken>())).ReturnsAsync(10);
        _users.Setup(r => r.GetByIdentityAsync(It.IsAny<string>(), "#known", It.IsAny<CancellationToken>())).ReturnsAsync(User(7));

        var attachment = await _manager.AttachUser("#known", _connection);

        Assert.Equal(AttachUserResult.Attached, attachment.Result);
    }

    [Fact]
    public async Task Attach_FollowsLimitsChangedWhileRunning()
    {
        Online(1, 2);
        _coordinator.ApplySettings(Model(maxOnlineUsers: 3, maxUsers: 10));

        var attachment = await _manager.AttachUser(null, _connection);

        Assert.Equal(AttachUserResult.Attached, attachment.Result);
    }

    [Fact]
    public async Task Attach_WithZeroLimits_IsNotLimited()
    {
        Online(1, 2, 3);
        _users.Setup(r => r.CountAsync(CoordinatorId, It.IsAny<CancellationToken>())).ReturnsAsync(1000);
        _coordinator.ApplySettings(Model(CoordinatorInstanceModel.NoUserLimit, CoordinatorInstanceModel.NoUserLimit));

        var attachment = await _manager.AttachUser(null, _connection);

        Assert.Equal(AttachUserResult.Attached, attachment.Result);
    }

    private void Online(params int[] ids)
    {
        foreach (var id in ids)
            _context.AttachedUsers[id] = User(id);
    }

    private static UserModel User(int id)
    {
        var user = UserModel.Create(CoordinatorId, $"user{id}", "127.0.0.1", DateTime.UtcNow);
        user.Id = id;
        return user;
    }

    private static CoordinatorInstanceModel Model(int maxOnlineUsers, int maxUsers) =>
        new(CoordinatorId, "coordinator", true, "127.0.0.1", 5000, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow,
            maxOnlineUsers, maxUsers, MotdSettings.Disabled, AccessListSettings.Open,
            new CoordinatorHeartbeatSettings(5, 40),
            CoordinatorMediaSettings.Default, CoordinatorUploadSettings.Default, CoordinatorRateLimitSettings.Default, []);
}
