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
    private readonly ConnectionsContext _connections = new();
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

        _manager = new UserManager(_context, NullLogger.Instance, CoordinatorId, _users.Object, _coordinator, _connections);
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

        // A second device does not consume another online user slot.
        var attachment = await _manager.AttachUser("#returning", _connection);

        Assert.Equal(AttachUserResult.Attached, attachment.Result);
        Assert.Equal(2, _manager.ConnectedUsersCount);
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

    [Fact]
    public async Task Attach_OfflineKnownUser_AtOnlineLimit_IsRejected()
    {
        Online(1, 2);
        _users.Setup(r => r.GetByIdentityAsync(CoordinatorId, "#offline", It.IsAny<CancellationToken>()))
            .ReturnsAsync(User(3));

        Assert.Equal(AttachUserResult.OnlineLimitReached,
            (await _manager.AttachUser("#offline", _connection)).Result);
        Assert.Equal(2, _manager.ConnectedUsersCount);
    }

    [Fact]
    public async Task Attach_ById_AlreadyOnline_AtOnlineLimit_Attaches()
    {
        Online(1, 2);
        _users.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(User(2));

        Assert.Equal(AttachUserResult.Attached, (await _manager.AttachUser(2, _connection)).Result);
        Assert.Equal(2, _manager.ConnectedUsersCount);
    }

    [Fact]
    public async Task Attach_ConcurrentNewUsers_RegistersOnlyTheOneTakingTheLastOnlinePlace()
    {
        Online(1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _users.Setup(r => r.AddAsync(It.IsAny<UserModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                entered.TrySetResult();
                await release.Task;
                return _nextId++;
            });

        var first = _manager.AttachUser(null, _connection);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = _manager.AttachUser(null, _connection);
        release.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, r => r.Result == AttachUserResult.Attached);
        Assert.Single(results, r => r.Result == AttachUserResult.OnlineLimitReached);
        Assert.Equal(2, _manager.ConnectedUsersCount);
        _users.Verify(r => r.AddAsync(It.IsAny<UserModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Attach_AfterDisconnect_ReusesOnlinePlace()
    {
        Online(1, 2);
        _manager.DetachUser(2, "test");

        Assert.Equal(AttachUserResult.Attached, (await _manager.AttachUser(null, _connection)).Result);
        Assert.Equal(2, _manager.ConnectedUsersCount);
    }

    [Fact]
    public async Task Attach_ConcurrentNewUsers_CannotExceedRegisteredLimit()
    {
        _coordinator.ApplySettings(Model(maxOnlineUsers: 10, maxUsers: 6));
        var registered = 5;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _users.Setup(r => r.CountAsync(CoordinatorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => registered);
        _users.Setup(r => r.AddAsync(It.IsAny<UserModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                entered.TrySetResult();
                await release.Task;
                registered++;
                return _nextId++;
            });

        var first = _manager.AttachUser(null, _connection);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = _manager.AttachUser(null, _connection);
        release.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, r => r.Result == AttachUserResult.Attached);
        Assert.Single(results, r => r.Result == AttachUserResult.UserLimitReached);
        Assert.Equal(6, registered);
        Assert.Equal(1, _manager.ConnectedUsersCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Attach_FiveDevices_CountAsOneUser_AndSixthIsRejected(bool byId)
    {
        _users.Setup(r => r.GetByIdentityAsync(CoordinatorId, "#known", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => User(1));
        _users.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(() => User(1));
        Task<UserAttachment> Attach(IClientConnection connection) => byId
            ? _manager.AttachUser(1, connection)
            : _manager.AttachUser("#known", connection);
        var devices = Enumerable.Range(0, 5).Select(i => Connection($"device-{i}")).ToArray();
        foreach (var device in devices)
            Assert.Equal(AttachUserResult.Attached, (await Attach(device)).Result);

        var sixth = Connection("sixth");
        Assert.Equal(AttachUserResult.ConnectionLimitReached, (await Attach(sixth)).Result);
        Assert.Null(sixth.UserId);
        Assert.Equal(5, _connections.UserConnections[1].Length);
        Assert.Equal(1, _manager.ConnectedUsersCount);

        // Another account can use the second online place, regardless of the first account's devices.
        Assert.Equal(AttachUserResult.Attached, (await _manager.AttachUser(null, Connection("other-user"))).Result);
        Assert.Equal(2, _manager.ConnectedUsersCount);
        Assert.Equal(AttachUserResult.OnlineLimitReached,
            (await _manager.AttachUser(null, Connection("full"))).Result);

        // Closing one device frees a connection place while the account remains online.
        Assert.False(_manager.DetachConnection(devices[0], "closed"));
        Assert.Equal(AttachUserResult.Attached, (await Attach(sixth)).Result);
        Assert.Equal(5, _connections.UserConnections[1].Length);
        Assert.Equal(2, _manager.ConnectedUsersCount);
    }

    [Fact]
    public async Task Attach_ConcurrentDevices_CannotExceedFive_EvenWithUnlimitedUsers()
    {
        _coordinator.ApplySettings(Model(0, 0));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _users.Setup(r => r.GetByIdentityAsync(CoordinatorId, "#known", It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                entered.TrySetResult();
                await release.Task;
                return User(1);
            });
        var first = _manager.AttachUser("#known", Connection("first"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var others = Enumerable.Range(0, 9)
            .Select(i => _manager.AttachUser("#known", Connection($"other-{i}"))).ToArray();
        release.SetResult();
        var results = await Task.WhenAll(others.Prepend(first));

        Assert.Equal(5, results.Count(r => r.Result == AttachUserResult.Attached));
        Assert.Equal(5, results.Count(r => r.Result == AttachUserResult.ConnectionLimitReached));
        Assert.Equal(5, _connections.UserConnections[1].Length);
        Assert.Equal(1, _manager.ConnectedUsersCount);
    }

    [Fact]
    public async Task Attach_AlreadyOnline_AfterOnlineLimitIsLowered_CanAddDevice()
    {
        Online(1, 2, 3);
        _users.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(User(1));

        Assert.Equal(AttachUserResult.Attached, (await _manager.AttachUser(1, Connection("extra"))).Result);
        Assert.Equal(3, _manager.ConnectedUsersCount);
    }

    private static IClientConnection Connection(string id)
    {
        var connection = new Mock<IClientConnection>();
        connection.SetupGet(c => c.ConnectionId).Returns(id);
        connection.SetupProperty(c => c.UserId);
        return connection.Object;
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
