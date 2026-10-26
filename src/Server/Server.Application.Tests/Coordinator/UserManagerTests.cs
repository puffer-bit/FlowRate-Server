using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Users;
using Server.Core.Models.User;
using Shared.Enums.User;

namespace Server.Application.Tests.Coordinator;

public class UserManagerTests
{
    private const string CoordinatorId = "coordinator";

    private readonly Mock<IUserRepository> _users = new();
    private readonly UserManagerContext _context = new();
    private readonly ConnectionsContext _connections = new();
    private readonly UserManager _manager;

    public UserManagerTests()
    {
        _manager = new UserManager(_context, NullLogger.Instance, CoordinatorId, _users.Object, connections: _connections);
    }

    [Fact]
    public async Task ChangeUsername_FreeName_RenamesSavesAndRaisesUpdated()
    {
        var alice = Attach(1, "alice");
        UserModel? updated = null;
        _manager.UserUpdated += user => updated = user;

        var result = await _manager.ChangeUsername(1, "  bob ");

        Assert.Equal(ChangeUsernameResult.NoError, result);
        Assert.Equal("bob", alice.Username);
        Assert.Same(alice, updated);
        _users.Verify(r => r.UpdateAsync(alice, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetAvatar_ChangesSavesAndRaisesUpdated()
    {
        var alice = Attach(1, "alice");
        var updates = 0;
        _manager.UserUpdated += _ => updates++;

        Assert.Equal(SetAvatarResult.NoError, await _manager.SetAvatar(1, "/media/avatars/0a"));
        Assert.Equal("/media/avatars/0a", alice.AvatarUrl);
        Assert.Equal(SetAvatarResult.NoError, await _manager.SetAvatar(1, "/media/avatars/0a"));
        Assert.Equal(SetAvatarResult.NoError, await _manager.SetAvatar(1, null));

        Assert.Null(alice.AvatarUrl);
        Assert.Equal(2, updates);
        _users.Verify(r => r.UpdateAsync(alice, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SetAvatar_UserNotConnected_ReturnsUserNotFound()
    {
        Assert.Equal(SetAvatarResult.UserNotFound, await _manager.SetAvatar(1, null));
    }

    [Fact]
    public async Task ChangeUsername_NameOfConnectedUser_IsRejected()
    {
        var alice = Attach(1, "alice");
        Attach(2, "bob");

        var result = await _manager.ChangeUsername(1, "bob");

        Assert.Equal(ChangeUsernameResult.UsernameAlreadyUsed, result);
        Assert.Equal("alice", alice.Username);
        _users.Verify(r => r.UpdateAsync(It.IsAny<UserModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangeUsername_NameStoredInDatabase_IsRejected()
    {
        Attach(1, "alice");
        _users.Setup(r => r.IsUsernameTakenAsync("bob", 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _manager.ChangeUsername(1, "bob");

        Assert.Equal(ChangeUsernameResult.UsernameAlreadyUsed, result);
    }

    [Fact]
    public async Task ChangeUsername_UnknownUser_ReturnsUserNotFound()
    {
        var result = await _manager.ChangeUsername(1, "bob");

        Assert.Equal(ChangeUsernameResult.UserNotFound, result);
    }

    [Fact]
    public async Task ChangeUsername_UserNotConnected_RenamesInDatabase()
    {
        var alice = Stored(1, "alice", CoordinatorId);
        UserModel? updated = null;
        _manager.UserUpdated += user => updated = user;

        var result = await _manager.ChangeUsername(1, "bob");

        Assert.Equal(ChangeUsernameResult.NoError, result);
        Assert.Equal("bob", alice.Username);
        Assert.Same(alice, updated);
        _users.Verify(r => r.UpdateAsync(alice, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangeUsername_UserOfOtherCoordinator_ReturnsUserNotFound()
    {
        Stored(1, "alice", "other");

        var result = await _manager.ChangeUsername(1, "bob");

        Assert.Equal(ChangeUsernameResult.UserNotFound, result);
        _users.Verify(r => r.UpdateAsync(It.IsAny<UserModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetState_ConnectedUser_ChangesSavesAndRaisesUpdated()
    {
        var alice = Attach(1, "alice");
        UserModel? updated = null;
        _manager.UserUpdated += user => updated = user;

        Assert.Equal(ModerateUserResult.NoError, await _manager.SetState(1, UserState.Suspended));

        Assert.Equal(UserState.Suspended, alice.State);
        Assert.Same(alice, updated);
        _users.Verify(r => r.UpdateAsync(alice, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetState_UserNotConnected_ChangesInDatabase()
    {
        var alice = Stored(1, "alice", CoordinatorId);

        Assert.Equal(ModerateUserResult.NoError, await _manager.SetState(1, UserState.Banned));

        Assert.True(alice.IsBanned);
        _users.Verify(r => r.UpdateAsync(alice, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(UserState.System)]
    [InlineData(UserState.Unknown)]
    public async Task SetState_NotAModerationState_IsRejected(UserState state)
    {
        var alice = Attach(1, "alice");

        Assert.Equal(ModerateUserResult.InvalidState, await _manager.SetState(1, state));
        Assert.Equal(UserState.Normal, alice.State);
    }

    [Fact]
    public async Task SetState_UserOfOtherCoordinator_ReturnsUserNotFound()
    {
        Stored(1, "alice", "other");

        Assert.Equal(ModerateUserResult.UserNotFound, await _manager.SetState(1, UserState.Banned));
        _users.Verify(r => r.UpdateAsync(It.IsAny<UserModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteUser_ConnectedUser_RemovesDetachesAndRaisesDeleted()
    {
        Attach(1, "alice");
        int? deleted = null;
        _manager.UserDeleted += id => deleted = id;

        Assert.Equal(ModerateUserResult.NoError, await _manager.DeleteUser(1));

        Assert.False(_manager.TryGetConnectedUser(1, out _));
        Assert.Equal(1, deleted);
        _users.Verify(r => r.RemoveAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteUser_UnknownUser_ReturnsUserNotFound()
    {
        Assert.Equal(ModerateUserResult.UserNotFound, await _manager.DeleteUser(1));
        _users.Verify(r => r.RemoveAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AttachUser_LooksTheIdentityUpInThisCoordinatorOnly()
    {
        var connection = new Mock<IClientConnection>();
        connection.SetupGet(c => c.IpEndpoint).Returns("127.0.0.1");

        var attachment = await _manager.AttachUser("#alice", connection.Object);

        Assert.Equal(AttachUserResult.UnknownIdentity, attachment.Result);
        _users.Verify(r => r.GetByIdentityAsync(CoordinatorId, "#alice", It.IsAny<CancellationToken>()), Times.Once);
        _users.Verify(r => r.GetByIdentityAsync(It.Is<string>(id => id != CoordinatorId), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AttachUser_BannedUser_IsRejected()
    {
        var alice = UserModel.Create(CoordinatorId, "alice", null, DateTime.UtcNow);
        alice.Id = 1;
        alice.Ban();
        _users.Setup(r => r.GetByIdentityAsync(It.IsAny<string>(), "#alice", It.IsAny<CancellationToken>())).ReturnsAsync(alice);
        var connection = new Mock<IClientConnection>();
        connection.SetupGet(c => c.IpEndpoint).Returns("127.0.0.1");

        var attachment = await _manager.AttachUser("#alice", connection.Object);

        Assert.Equal(AttachUserResult.Banned, attachment.Result);
        Assert.False(_manager.TryGetConnectedUser(1, out _));
    }

    [Fact]
    public async Task AttachUser_UnknownIdentity_IsRejectedWithoutCreatingUser()
    {
        var connection = new Mock<IClientConnection>();

        var attachment = await _manager.AttachUser("#nobody", connection.Object);

        Assert.Equal(AttachUserResult.UnknownIdentity, attachment.Result);
        Assert.Equal(0, _manager.ConnectedUsersCount);
        _users.Verify(r => r.AddAsync(It.IsAny<UserModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SetStatus_ConnectedUser_ChangesSavesAndRaisesUpdated()
    {
        var alice = Attach(1, "alice");
        UserModel? updated = null;
        _manager.UserUpdated += user => updated = user;

        var result = await _manager.SetStatus(1, UserStatus.DoNotDisturb);

        Assert.Equal(SetUserStatusResult.NoError, result);
        Assert.Equal(UserStatus.DoNotDisturb, alice.Status);
        Assert.Same(alice, updated);
        _users.Verify(r => r.UpdateAsync(alice, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(UserStatus.Offline)]
    [InlineData((UserStatus)42)]
    public async Task SetStatus_NotAChoice_IsRejected(UserStatus status)
    {
        var alice = Attach(1, "alice");

        Assert.Equal(SetUserStatusResult.InvalidStatus, await _manager.SetStatus(1, status));
        Assert.Equal(UserStatus.Online, alice.Status);
    }

    [Fact]
    public async Task SetStatus_UserNotConnected_ReturnsUserNotFound()
    {
        Assert.Equal(SetUserStatusResult.UserNotFound, await _manager.SetStatus(1, UserStatus.Away));
    }

    [Fact]
    public async Task AttachAndDetach_ShowTheUserOnlineThenOffline()
    {
        var alice = UserModel.Create(CoordinatorId, "alice", null, DateTime.UtcNow);
        alice.Id = 1;
        alice.SetStatus(UserStatus.Away);
        _users.Setup(r => r.GetByIdentityAsync(It.IsAny<string>(), "#alice", It.IsAny<CancellationToken>())).ReturnsAsync(alice);
        var connection = new Mock<IClientConnection>();
        UserModel? disconnected = null;
        _manager.UserDisconnected += (user, _) => disconnected = user;

        await _manager.AttachUser("#alice", connection.Object);
        Assert.Equal(UserStatus.Away, alice.Presence);

        _manager.DetachUser(1, "bye");
        Assert.Same(alice, disconnected);
        Assert.Equal(UserStatus.Offline, alice.Presence);
        Assert.Equal(UserStatus.Away, alice.Status);
    }

    [Fact]
    public async Task DetachConnection_UserOnAnotherDevice_StaysOnlineUntilTheLastConnectionCloses()
    {
        var alice = UserModel.Create(CoordinatorId, "alice", null, DateTime.UtcNow);
        alice.Id = 1;
        _users.Setup(r => r.GetByIdentityAsync(It.IsAny<string>(), "#alice", It.IsAny<CancellationToken>())).ReturnsAsync(alice);
        var phone = Connection("phone");
        var desktop = Connection("desktop");
        var connects = 0;
        var disconnects = 0;
        _manager.UserConnected += _ => connects++;
        _manager.UserDisconnected += (_, _) => disconnects++;

        await _manager.AttachUser("#alice", phone.Object);
        await _manager.AttachUser("#alice", desktop.Object);

        Assert.Equal(1, connects);
        Assert.Equal(["phone", "desktop"], _connections.UserConnections[1]);
        Assert.Equal(1, desktop.Object.UserId);

        Assert.False(_manager.DetachConnection(phone.Object, "Connection is closed"));
        Assert.True(_manager.TryGetConnectedUser(1, out _));
        Assert.Equal(0, disconnects);

        Assert.True(_manager.DetachConnection(desktop.Object, "Connection is closed"));
        Assert.False(_manager.TryGetConnectedUser(1, out _));
        Assert.False(_connections.UserConnections.ContainsKey(1));
        Assert.Equal(1, disconnects);
    }

    [Fact]
    public async Task AttachUser_SecondDevice_KeepsTheUserAttachedAsTheyAre()
    {
        var first = Stored(1, "alice", CoordinatorId);
        var second = Stored(1, "alice", CoordinatorId);
        _users.SetupSequence(r => r.GetByIdentityAsync(It.IsAny<string>(), "#alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(first)
            .ReturnsAsync(second);

        await _manager.AttachUser("#alice", Connection("phone").Object);
        first.SetStatus(UserStatus.Away);
        var attachment = await _manager.AttachUser("#alice", Connection("desktop").Object);

        Assert.Same(first, attachment.User);
        Assert.Equal(UserStatus.Away, attachment.User!.Status);
    }

    [Fact]
    public async Task AttachUser_ConnectionClosedDuringTheLogin_AttachesNothing()
    {
        var alice = Stored(1, "alice", CoordinatorId);
        _users.Setup(r => r.GetByIdentityAsync(It.IsAny<string>(), "#alice", It.IsAny<CancellationToken>())).ReturnsAsync(alice);
        var connection = Connection("closed");
        connection.SetupGet(c => c.IsClosed).Returns(true);

        var attachment = await _manager.AttachUser("#alice", connection.Object);

        Assert.Equal(AttachUserResult.ConnectionClosed, attachment.Result);
        Assert.False(_manager.TryGetConnectedUser(1, out _));
        Assert.False(_connections.UserConnections.ContainsKey(1));
        Assert.Null(connection.Object.UserId);
    }

    [Fact]
    public async Task DetachConnection_NotOfTheUser_DetachesNothing()
    {
        var alice = Stored(1, "alice", CoordinatorId);
        _users.Setup(r => r.GetByIdentityAsync(It.IsAny<string>(), "#alice", It.IsAny<CancellationToken>())).ReturnsAsync(alice);
        await _manager.AttachUser("#alice", Connection("desktop").Object);
        var stranger = Connection("stranger");
        stranger.Object.UserId = 1;

        Assert.False(_manager.DetachConnection(stranger.Object, "Connection is closed"));
        Assert.True(_manager.TryGetConnectedUser(1, out _));
    }

    private static Mock<IClientConnection> Connection(string id)
    {
        var connection = new Mock<IClientConnection>();
        connection.SetupGet(c => c.ConnectionId).Returns(id);
        connection.SetupProperty(c => c.UserId);
        return connection;
    }

    [Fact]
    public async Task GetAllUsersAsync_ConnectedUser_IsTheAttachedOne()
    {
        var stored = Stored(1, "alice", CoordinatorId);
        var alice = Attach(1, "alice");
        var bob = Stored(2, "bob", CoordinatorId);
        _users.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, UserModel> { [1] = stored, [2] = bob });

        var users = await _manager.GetAllUsersAsync();

        Assert.Same(alice, users[1]);
        Assert.Same(bob, users[2]);
    }

    private UserModel Stored(int id, string username, string coordinatorId)
    {
        var user = UserModel.Create(coordinatorId, username, null, DateTime.UtcNow);
        user.Id = id;
        _users.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return user;
    }

    private UserModel Attach(int id, string username)
    {
        var user = UserModel.Create(CoordinatorId, username, null, DateTime.UtcNow);
        user.Id = id;
        user.SetConnected(true);
        _context.AttachedUsers[id] = user;
        return user;
    }
}
