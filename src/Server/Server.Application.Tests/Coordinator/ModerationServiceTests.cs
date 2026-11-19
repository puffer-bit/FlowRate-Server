using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Moderation;
using Server.Application.Coordinator.Roles;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Users;
using Server.Core.Models.Moderation;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Notifications.CoordinatorInfo;
using Shared.Models.Requests.Coordinator;

namespace Server.Application.Tests.Coordinator;

public class ModerationServiceTests
{
    private const string Coordinator = "coordinator";
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly MemoryBans _bans = new();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<IUserRepository> _repository = new();
    private readonly Mock<IConnectionManager> _connections = new();
    private readonly Mock<ICoordinatorPermissions> _permissions = new();
    private readonly Dictionary<int, UserModel> _models = new();
    private readonly List<Mock<IClientConnection>> _devices = [];
    private readonly ModerationService _service;

    public ModerationServiceTests()
    {
        AddUser(1, "192.0.2.1");
        AddUser(2, "192.0.2.2");
        _users.Setup(x => x.GetUserAsync(It.IsAny<int>())).ReturnsAsync((int id) => _models.GetValueOrDefault(id));
        _users.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(() => _models);
        _users.Setup(x => x.SetState(It.IsAny<int>(), It.IsAny<UserState>()))
            .ReturnsAsync((int id, UserState state) =>
            {
                if (state == UserState.Banned) _models[id].Ban();
                else if (state == UserState.Suspended) _models[id].Suspend();
                else _models[id].Reinstate();
                return (ErrorCode?)null;
            });
        _repository.Setup(x => x.GetByIdentityAsync(Coordinator, "alice", default)).ReturnsAsync(() => _models[1]);
        _connections.Setup(x => x.GetAllConnections()).Returns(() => _devices.Select(d => d.Object).ToList());
        _permissions.Setup(x => x.Has(10, Permission.BanMembers)).Returns(true);
        _permissions.Setup(x => x.HighestPositionOfAsync(It.IsAny<int>())).ReturnsAsync((int id) => id == 10 ? 5 : 0);
        _service = new ModerationService(Coordinator, _bans, _repository.Object, _users.Object, _connections.Object,
            Mock.Of<IHybridSessionManager>(), _permissions.Object, _time, NullLogger.Instance);
    }

    private void AddUser(int id, string ip)
    {
        var user = UserModel.Create(Coordinator, $"user{id}", ip, _time.GetUtcNow().UtcDateTime);
        user.Id = id;
        _models[id] = user;
    }

    private Mock<IClientConnection> Device(int? userId, string ip)
    {
        var connection = new Mock<IClientConnection>();
        connection.Setup(x => x.UserId).Returns(userId);
        connection.Setup(x => x.IpEndpoint).Returns(ip);
        _devices.Add(connection);
        return connection;
    }

    private CoordinatorSetUserStateRequest Ban(BanKind kind = BanKind.Account, DateTime? expires = null) =>
        new(1, UserState.Banned, kind, "  Abuse in chat  ", expires);

    private Task<LoginOutcome> Admit(IClientConnection connection, int? id = 1) =>
        _service.AdmitAsync(null, id, connection, () => Task.FromResult(
            new LoginOutcome(null, _models[id ?? 1], null, AttachUserResult.Attached, "token")));

    [Fact]
    public async Task AccountBan_StoresDetails_ClosesEveryDevice_AndDoesNotBanOtherAccountsOnTheAddress()
    {
        var first = Device(1, "192.0.2.1");
        var second = Device(1, "192.0.2.8");
        var other = Device(2, "192.0.2.1");
        Assert.Null(await _service.SetStateAsync(10, Ban()));
        var entry = Assert.Single(await _service.ListAsync());
        Assert.Equal("Abuse in chat", entry.Reason);
        Assert.Equal(10, entry.CreatedBy);
        foreach (var device in new[] { first, second })
        {
            device.Verify(x => x.Send(It.Is<CoordinatorUserBannedNotification>(n => n.Reason == entry.Reason)), Times.Once);
            device.Verify(x => x.Close(ConnectionCloseReason.Normal), Times.Once);
        }
        other.Verify(x => x.Close(It.IsAny<ConnectionCloseReason>()), Times.Never);
        Assert.Equal(ErrorCode.Banned, (await Admit(first.Object)).Error);
        Assert.Null((await Admit(other.Object, 2)).Error);
    }

    [Fact]
    public async Task IpBan_NormalizesMappedAddresses_BlocksNewAccounts_AndOnlyClosesMatchingConnections()
    {
        var blocked = Device(2, "::ffff:192.0.2.1");
        var pending = Device(null, "192.0.2.1");
        var other = Device(1, "192.0.2.8");
        Assert.Null(await _service.SetStateAsync(-1, Ban(BanKind.Ip)));
        Assert.NotNull(await _service.CheckAddressAsync("::ffff:192.0.2.1"));
        Assert.Null(await _service.CheckAddressAsync("192.0.2.8"));
        blocked.Verify(x => x.Close(ConnectionCloseReason.Normal), Times.Once);
        pending.Verify(x => x.Close(ConnectionCloseReason.Normal), Times.Once);
        other.Verify(x => x.Close(It.IsAny<ConnectionCloseReason>()), Times.Never);
        Assert.Equal(ErrorCode.Banned, (await Admit(pending.Object, null)).Error);
        Assert.Null((await Admit(other.Object)).Error);
        Assert.False(_models[1].IsBanned);
    }

    [Theory]
    [InlineData(BanKind.Hardware, "reason")]
    [InlineData((BanKind)99, "reason")]
    public async Task InvalidOrUnimplementedBan_IsRejectedWithoutSaving(BanKind kind, string reason)
    {
        Assert.Equal(ErrorCode.InvalidValue, await _service.SetStateAsync(10, Ban(kind) with { Reason = reason }));
        Assert.Empty(_bans.Items);
    }

    [Theory]
    [InlineData(BanKind.Account, null)]
    [InlineData(BanKind.Account, "")]
    [InlineData(BanKind.Account, "  ")]
    [InlineData(BanKind.Ip, null)]
    public async Task OptionalReason_IsStoredEmpty_AndBanStillBlocksAdmission(BanKind kind, string? reason)
    {
        var device = Device(1, "192.0.2.1");
        Assert.Null(await _service.SetStateAsync(10, Ban(kind) with { Reason = reason }));
        Assert.Equal(string.Empty, Assert.Single(_bans.Items).Reason);
        var denied = await Admit(device.Object);
        Assert.Equal(ErrorCode.Banned, denied.Error);
        Assert.Equal(string.Empty, denied.BanReason);
        if (kind == BanKind.Ip) Assert.NotNull(await _service.CheckAddressAsync("192.0.2.1"));
    }

    [Fact]
    public async Task OversizedReason_IsRejectedWithoutSaving()
    {
        Assert.Equal(ErrorCode.InvalidValue, await _service.SetStateAsync(10, Ban() with { Reason = new string('a', 1001) }));
        Assert.Empty(_bans.Items);
    }

    [Fact]
    public async Task InvalidExpiryAndUnknownIp_AreRejected()
    {
        Assert.Equal(ErrorCode.InvalidValue, await _service.SetStateAsync(10, Ban(expires: _time.GetUtcNow().UtcDateTime)));
        Assert.Equal(ErrorCode.InvalidValue, await _service.SetStateAsync(10, Ban(BanKind.Ip) with { Address = "invalid" }));
        Assert.Empty(_bans.Items);
    }

    [Fact]
    public async Task Expiration_RestoresFrozenState_AndDeletesTheBan()
    {
        _models[1].Suspend();
        Assert.Null(await _service.SetStateAsync(10, Ban(expires: _time.GetUtcNow().UtcDateTime.AddHours(1))));
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Null((await Admit(Device(1, "192.0.2.1").Object)).Error);
        Assert.Equal(UserState.Suspended, _models[1].State);
        Assert.Empty(await _service.ListAsync());
        Assert.Empty(_bans.Items);
    }

    [Fact]
    public async Task Revoke_DeletesTheBan_AndRestoresState()
    {
        Assert.Null(await _service.SetStateAsync(10, Ban()));
        var ban = Assert.Single(await _service.ListAsync());
        Assert.Null(await _service.RevokeAsync(10, ban.Id));
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(ErrorCode.UserNotFound, await _service.RevokeAsync(10, ban.Id));
        Assert.Empty(await _service.ListAsync());
        Assert.Empty(_bans.Items);
        Assert.False(_models[1].IsBanned);
    }

    [Fact]
    public async Task LegacyUnbanAction_AlsoRestoresPreviousFreeze_AndRefreshesTheIpCache()
    {
        _models[1].Suspend();
        Assert.Null(await _service.SetStateAsync(10, Ban()));
        Assert.Null(await _service.SetStateAsync(10, new CoordinatorSetUserStateRequest(1, UserState.Normal)));
        Assert.Equal(UserState.Suspended, _models[1].State);
        Assert.Null(await _service.SetStateAsync(10, Ban(BanKind.Ip)));
        Assert.NotNull(await _service.CheckAddressAsync("192.0.2.1"));
        var entry = Assert.Single(await _service.ListAsync());
        Assert.Null(await _service.RevokeAsync(10, entry.Id));
        Assert.Null(await _service.CheckAddressAsync("192.0.2.1"));
    }

    [Fact]
    public async Task SharedIp_CannotBeUsedToBanYourselfOrAHigherRole()
    {
        AddUser(10, "192.0.2.1");
        Assert.Equal(ErrorCode.InsufficientPermissions, await _service.SetStateAsync(10, Ban(BanKind.Ip)));
        _models.Remove(10);
        _permissions.Setup(x => x.HighestPositionOfAsync(2)).ReturnsAsync(6);
        Device(2, "192.0.2.1");
        Assert.Equal(ErrorCode.InsufficientPermissions, await _service.SetStateAsync(10, Ban(BanKind.Ip)));
        Assert.Empty(_bans.Items);
    }

    [Fact]
    public async Task ConcurrentLogin_IsIncludedInTheDisconnectPass_AndLaterLoginIsRejectedWithReason()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var device = Device(1, "192.0.2.1");
        var login = _service.AdmitAsync(null, 1, device.Object, async () =>
        {
            entered.SetResult();
            await release.Task;
            return new LoginOutcome(null, _models[1], null, AttachUserResult.Attached, "token");
        });
        await entered.Task;
        var ban = _service.SetStateAsync(10, Ban());
        Assert.False(ban.IsCompleted);
        release.SetResult();
        await login;
        Assert.Null(await ban);
        device.Verify(x => x.Close(ConnectionCloseReason.Normal), Times.Once);
        var denied = await Admit(device.Object);
        Assert.Equal(ErrorCode.Banned, denied.Error);
        Assert.Equal("Abuse in chat", denied.BanReason);
    }

    [Fact]
    public async Task Restart_StillChecksPersistedBans_AndRevokeCannotCrossCoordinators()
    {
        Assert.Null(await _service.SetStateAsync(10, Ban(BanKind.Ip)));
        var id = Assert.Single(_bans.Items).Id;
        var restarted = new ModerationService(Coordinator, _bans, _repository.Object, _users.Object,
            _connections.Object, Mock.Of<IHybridSessionManager>(), _permissions.Object, _time, NullLogger.Instance);
        Assert.NotNull(await restarted.CheckAddressAsync("192.0.2.1"));
        var other = new ModerationService("other", _bans, _repository.Object, _users.Object,
            _connections.Object, Mock.Of<IHybridSessionManager>(), _permissions.Object, _time, NullLogger.Instance);
        Assert.Equal(ErrorCode.UserNotFound, await other.RevokeAsync(-1, id));
        Assert.NotNull(await restarted.CheckAddressAsync("192.0.2.1"));
    }

    private class MemoryBans : IBanRepository
    {
        public List<GlobalBan> Items { get; } = [];
        public Task<List<GlobalBan>> GetAsync(string coordinatorId, CancellationToken ct = default) =>
            Task.FromResult(Items.Where(x => x.CoordinatorId == coordinatorId).ToList());
        public Task AddAsync(GlobalBan ban, CancellationToken ct = default) { Items.Add(ban); return Task.CompletedTask; }
        public Task DeleteAsync(string coordinatorId, Guid id, CancellationToken ct = default)
        {
            Items.RemoveAll(x => x.CoordinatorId == coordinatorId && x.Id == id);
            return Task.CompletedTask;
        }
    }
}
