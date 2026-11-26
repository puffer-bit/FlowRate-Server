using Microsoft.Extensions.Time.Testing;
using Moq;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Connections;
using Server.Application.Tests.Fakes;
using Server.Core.Models.User;
using Xunit;

namespace Server.Application.Tests.Coordinator;

/// <summary>A device ends after its lifetime offline: being online gives it another lifetime.</summary>
public class DeviceKeepAliveTests
{
    private const string Coordinator = "coordinator";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryUserSessionRepository _devices = new();
    private readonly Mock<IConnectionManager> _connections = new();
    private readonly List<IClientConnection> _online = [];
    private readonly UserSessions _sessions;

    public DeviceKeepAliveTests()
    {
        _connections.Setup(c => c.GetAllConnections()).Returns(() => _online);
        _sessions = new UserSessions(_devices, _connections.Object, Coordinator, _time);
    }

    private DateTimeOffset Now => _time.GetUtcNow();

    private async Task<UserSession> AddDevice(DateTimeOffset expiresAt, bool isTemporary = false, bool online = false)
    {
        var lastLogin = Now.AddDays(-80);
        var device = new UserSession(Guid.NewGuid(), Coordinator, 1, null, null, "bot", "Linux", "0.7.1", lastLogin,
            lastLogin, expiresAt, null, [1, 2, 3], isTemporary);
        await _devices.AddAsync(device);

        if (online)
        {
            var connection = new Mock<IClientConnection>();
            connection.Setup(c => c.UserSessionId).Returns(device.Id);
            _online.Add(connection.Object);
        }

        return device;
    }

    private UserSession Stored(Guid id) => _devices.All.Single(x => x.Id == id);

    [Fact]
    public async Task DevicesOnline_LiveOnForAnotherLifetime_AndOfflineOnesDoNot()
    {
        var online = await AddDevice(Now.AddDays(10), online: true);
        var temporary = await AddDevice(Now.AddHours(2), isTemporary: true, online: true);
        var offline = await AddDevice(Now.AddDays(10));

        await _sessions.KeepOnlineAliveAsync();

        Assert.Equal(Now + UserSessions.Lifetime, Stored(online.Id).ExpiresAt);
        Assert.Equal(Now + UserSessions.TemporaryLifetime, Stored(temporary.Id).ExpiresAt);
        Assert.Equal(offline.ExpiresAt, Stored(offline.Id).ExpiresAt);
    }

    [Fact]
    public async Task KeepingAlive_LeavesTheLastLoginAsItIs()
    {
        var device = await AddDevice(Now.AddDays(10), online: true);

        await _sessions.KeepOnlineAliveAsync();

        Assert.Equal(device.LastUsedAt, Stored(device.Id).LastUsedAt);
    }

    [Fact]
    public async Task ExpiredDevice_IsNotBroughtBack()
    {
        var expired = await AddDevice(Now.AddMinutes(-1), online: true);

        await _sessions.KeepAliveAsync([expired.Id]);

        Assert.Equal(expired.ExpiresAt, Stored(expired.Id).ExpiresAt);
    }

    [Fact]
    public async Task DisconnectedDevice_LivesOnFromTheDisconnect()
    {
        var device = await AddDevice(Now.AddDays(10));
        _time.Advance(TimeSpan.FromMinutes(30));

        await _sessions.KeepAliveAsync([device.Id]);

        Assert.Equal(Now + UserSessions.Lifetime, Stored(device.Id).ExpiresAt);
    }
}
