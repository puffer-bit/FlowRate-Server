using Shared.Enums;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Handlers.Users.Security;
using Server.Application.Coordinator.Messaging;
using Server.Application.Orchestrator.Security;
using Server.Application.Tests.Fakes;
using Server.Core.Models.User;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Notifications.CoordinatorInfo;
using Shared.Models.Requests.Coordinator.Security;
using Shared.Models.Responses;
using Shared.Models.Responses.Coordinator.Security;

namespace Server.Application.Tests.Coordinator;

public class UserSessionHandlersTests
{
    private const int UserId = 1;
    private const string Coordinator = "coordinator";

    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly InMemoryUserSessionRepository _repository = new();
    private readonly Mock<IConnectionManager> _connections = new();
    private readonly Mock<IMessageSender> _sender = new();
    private readonly Mock<IUserSecurityService> _security = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly List<BaseMessage> _sent = [];
    private readonly Dictionary<Guid, Mock<IClientConnection>> _devices = [];
    private readonly UserSessions _sessions;

    private readonly Guid _current;
    private readonly Guid _laptop;
    private readonly Guid _phone;

    public UserSessionHandlersTests()
    {
        _sessions = new UserSessions(_repository, _connections.Object, Coordinator, _time);
        _current = AddSession("desk");
        _laptop = AddSession("laptop");
        _phone = AddSession("phone");

        _connections.Setup(c => c.GetUserConnections(UserId))
            .Returns(() => _devices.Values.Select(device => device.Object).ToList());
        var current = _devices[_current].Object;
        _connections.Setup(c => c.TryGetConnectionById("desk", out current)).Returns(true);
        _sender.Setup(s => s.SendMessageById("desk", It.IsAny<BaseMessage>()))
            .Callback((string _, BaseMessage message) => _sent.Add(message));
    }

    private Guid AddSession(string device)
    {
        var id = Guid.NewGuid();
        var now = _time.GetUtcNow().UtcDateTime;
        _repository.AddAsync(new UserSession(id, Coordinator, UserId, SecretHash.Of(device), null, device, "Linux",
            "0.8.0", now, now, now + UserSessions.Lifetime, null));

        var connection = new Mock<IClientConnection>();
        connection.Setup(c => c.ConnectionId).Returns(device);
        connection.Setup(c => c.UserSessionId).Returns(id);
        _devices[id] = connection;
        return id;
    }

    private async Task<T> Send<TRequest, T>(IMessageHandler<TRequest> handler, TRequest request)
        where TRequest : Shared.Models.Requests.Request where T : BaseMessage
    {
        await handler.HandleMessage(request with { SenderId = UserId }, "desk");
        return Assert.IsAssignableFrom<T>(_sent[^1]);
    }

    private UserSessionRevokeHandler RevokeHandler() => new(_sender.Object, _connections.Object, _sessions);

    private UserIdentityResetHandler ResetHandler() => new(_sender.Object, _connections.Object,
        new UserSecurityReplies(_security.Object, Coordinator), _users.Object, _sessions);

    private void VerifyEnded(Guid session, UserSessionEndReason reason)
    {
        _devices[session].Verify(c => c.Send(It.Is<CoordinatorUserSessionEndedNotification>(n => n.Reason == reason)));
        _devices[session].Verify(c => c.Close(It.IsAny<ConnectionCloseReason>()));
    }

    private void VerifyUntouched(Guid session) =>
        _devices[session].Verify(c => c.Close(It.IsAny<ConnectionCloseReason>()), Times.Never);

    [Fact]
    public async Task Sessions_AreListed_WithTheCurrentOneMarked()
    {
        var response = await Send<UserSessionsRequest, UserSessionsResponse>(
            new UserSessionsHandler(_sender.Object, _connections.Object, _sessions), new UserSessionsRequest());
        Assert.Equal(3, response.Sessions!.Count);
        Assert.Equal(_current, Assert.Single(response.Sessions, s => s.IsCurrent).Id);
        Assert.All(response.Sessions, s => Assert.True(s.IsOnline));
    }

    [Fact]
    public async Task EndingASession_DisconnectsOnlyItsDevice()
    {
        var response = await Send<UserSessionRevokeRequest, UserSessionsResponse>(RevokeHandler(),
            new UserSessionRevokeRequest(_laptop));
        Assert.DoesNotContain(response.Sessions!, s => s.Id == _laptop);
        VerifyEnded(_laptop, UserSessionEndReason.Revoked);
        VerifyUntouched(_current);
        VerifyUntouched(_phone);
    }

    [Fact]
    public async Task EndingAllOthers_KeepsTheCurrentSession()
    {
        var response = await Send<UserSessionRevokeRequest, UserSessionsResponse>(RevokeHandler(),
            new UserSessionRevokeRequest(null));

        Assert.Equal(_current, Assert.Single(response.Sessions!).Id);
        VerifyEnded(_laptop, UserSessionEndReason.Revoked);
        VerifyEnded(_phone, UserSessionEndReason.Revoked);
        VerifyUntouched(_current);
    }

    [Fact]
    public async Task TheCurrentSession_OrAnUnknownOne_IsNotEnded()
    {
        foreach (var id in new[] { _current, Guid.NewGuid() })
        {
            var response = await Send<UserSessionRevokeRequest, Response>(RevokeHandler(),
                new UserSessionRevokeRequest(id));
            Assert.Equal(ErrorCode.SessionNotFound, response.Error());
        }

        Assert.Equal(3, _repository.All.Count);
    }

    [Fact]
    public async Task IdentityReset_WithoutProof_ChangesNothing()
    {
        var response = await Send<UserIdentityResetRequest, Response>(ResetHandler(),
            new UserIdentityResetRequest(null));

        Assert.Equal(ErrorCode.ProofRequired, response.Error());
        _users.Verify(u => u.SetIdentityAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Equal(3, _repository.All.Count);
    }

    [Fact]
    public async Task IdentityReset_GivesANewIdentity_AndEndsTheOtherSessions()
    {
        _security.Setup(s => s.CheckProofAsync(UserId, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var response = await Send<UserIdentityResetRequest, UserIdentityResetResponse>(ResetHandler(),
            new UserIdentityResetRequest(null));
        Assert.StartsWith("#", response.UserIdentity);
        _users.Verify(u => u.SetIdentityAsync(UserId, response.UserIdentity!, It.IsAny<CancellationToken>()));
        Assert.Equal(_current, Assert.Single(_repository.All).Id);
        VerifyEnded(_laptop, UserSessionEndReason.IdentityReset);
        VerifyEnded(_phone, UserSessionEndReason.IdentityReset);
        VerifyUntouched(_current);
    }
}
