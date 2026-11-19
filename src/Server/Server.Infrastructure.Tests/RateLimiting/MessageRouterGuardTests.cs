using Server.Application.Coordinator.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.Auth;
using Shared.Models.Requests.SessionActions.Generic;
using Shared.Models.Requests.SessionActions.Generic.Models;
using Shared.Models.Requests.SessionActions.Generic.Models.SessionData;
using Shared.Models.Responses;
using Xunit;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Users;
using Server.Application.Coordinator.Connections.RateLimiting;
using Server.Application.Coordinator.Roles;
using Server.Application.Coordinator.Sessions.Hybrid;

namespace Server.Infrastructure.Tests.RateLimiting;

public class MessageRouterGuardTests
{
    private readonly FakeConnection _connection = new("connection-1", "10.0.0.7");
    private readonly Dictionary<Type, int> _handled = new();
    private readonly MessageRouter _router;

    public MessageRouterGuardTests()
    {
        IClientConnection? found = _connection;
        var connections = new Mock<IConnectionManager>();
        connections.Setup(m => m.TryGetConnectionById("connection-1", out found)).Returns(true);

        _router = new MessageRouter(NullLogger.Instance,
            [Handler<CreateSessionRequest>(), Handler<DeleteSessionRequest>(), Handler<UserAuthRequest>()],
            connections.Object);
    }

    private IMessageHandler Handler<T>()
    {
        var handler = new Mock<IMessageHandler>();
        handler.SetupGet(h => h.PayloadType).Returns(typeof(T));
        handler.Setup(h => h.HandleMessage(It.IsAny<object?>(), It.IsAny<string>()))
            .Callback(() => _handled[typeof(T)] = _handled.GetValueOrDefault(typeof(T)) + 1)
            .ReturnsAsync(HandleMessageResult.NoError);
        return handler.Object;
    }

    private void Route(BaseMessage message, int senderId = 5)
    {
        message.SenderId = senderId;
        _router.RouteIncomingMessage(message, "connection-1");
    }

    private static CreateSessionRequest CreateSession(string name = "room") =>
        new(new CreateSessionModel(name, 10, new ChatSessionCreateData()));

    [Fact]
    public void RequestWithoutPermission_IsRejectedWithoutReachingTheHandler()
    {
        IClientConnection? found = _connection;
        var connections = new Mock<IConnectionManager>();
        connections.Setup(m => m.TryGetConnectionById("connection-1", out found)).Returns(true);
        var permissions = new Mock<ICoordinatorPermissions>();
        permissions.Setup(p => p.Has(5, Permission.CreateSessions)).Returns(false);
        var router = new MessageRouter(NullLogger.Instance, [Handler<CreateSessionRequest>()], connections.Object,
            authorizer: new RequestAuthorizer(permissions.Object, Mock.Of<IHybridSessionManager>(),
                Mock.Of<ISessionManager>()));

        var request = CreateSession();
        request.SenderId = 5;
        router.RouteIncomingMessage(request, "connection-1");

        Assert.False(_handled.ContainsKey(typeof(CreateSessionRequest)));
        var rejected = Assert.IsType<ErrorResponse>(Assert.Single(_connection.Sent));
        Assert.Equal(request.RequestId, rejected.RequestId);
        Assert.Equal(ErrorCode.InsufficientPermissions, rejected.Code);
    }

    [Fact]
    public void ValidRequest_ReachesItsHandler()
    {
        Route(CreateSession());

        Assert.Equal(1, _handled[typeof(CreateSessionRequest)]);
        Assert.Empty(_connection.Sent);
    }

    [Fact]
    public void InvalidRequest_IsRejectedWithoutReachingTheHandler()
    {
        var request = CreateSession(new string('x', 500));
        Route(request);

        Assert.False(_handled.ContainsKey(typeof(CreateSessionRequest)));
        var rejected = Assert.IsType<ErrorResponse>(Assert.Single(_connection.Sent));
        Assert.Equal(request.RequestId, rejected.RequestId);
        Assert.Equal(ErrorCode.InvalidRequest, rejected.Code);
        Assert.Contains("Name", rejected.Details);
    }

    [Fact]
    public void RequestOverTheRateLimit_IsRejected()
    {
        for (var i = 0; i < 10; i++)
            Route(CreateSession());
        var eleventh = new DeleteSessionRequest(3);
        Route(eleventh);

        Assert.Equal(10, _handled[typeof(CreateSessionRequest)]);
        Assert.False(_handled.ContainsKey(typeof(DeleteSessionRequest)));
        var rejected = Assert.IsType<ErrorResponse>(Assert.Single(_connection.Sent));
        Assert.Equal(eleventh.RequestId, rejected.RequestId);
        Assert.Equal(ErrorCode.RateLimited, rejected.Code);
        Assert.Null(_connection.CloseReason);
    }

    [Fact]
    public void TwentyRejections_CloseTheConnection()
    {
        for (var i = 0; i < RequestRateLimits.Default.MaxRejectionsPerWindow - 1; i++)
            Route(new DeleteSessionRequest(0));
        Assert.Null(_connection.CloseReason);

        Route(new DeleteSessionRequest(0));

        Assert.Equal(ConnectionCloseReason.TooManyRejectedRequests, _connection.CloseReason);
    }

    [Fact]
    public void AuthenticationAttempts_AreLimitedPerIp()
    {
        for (var i = 0; i < 5; i++)
            Route(new UserAuthRequest("p", "u"), senderId: 0);
        Route(new UserAuthRequest("p", "u"), senderId: 0);

        Assert.Equal(5, _handled[typeof(UserAuthRequest)]);
        Assert.Equal(ErrorCode.RateLimited, Assert.IsType<ErrorResponse>(Assert.Single(_connection.Sent)).Code);
    }

    [Fact]
    public void UnauthenticatedRequest_ClosesTheConnection()
    {
        Route(CreateSession(), senderId: 0);

        Assert.False(_handled.ContainsKey(typeof(CreateSessionRequest)));
        Assert.Equal(ConnectionCloseReason.Normal, _connection.CloseReason);
    }

    [Fact]
    public void MalformedMessagesTooOften_CloseTheConnection()
    {
        for (var i = 0; i < RequestRateLimits.Default.MaxRejectionsPerWindow - 1; i++)
            _router.RejectMalformedMessage("connection-1");
        Assert.Null(_connection.CloseReason);

        _router.RejectMalformedMessage("connection-1");

        Assert.Equal(ConnectionCloseReason.TooManyRejectedRequests, _connection.CloseReason);
    }

    private sealed class FakeConnection(string connectionId, string ip) : IClientConnection
    {
        public event Action<string>? Disconnected;
        public List<BaseMessage> Sent { get; } = new();
        public ConnectionCloseReason? CloseReason { get; private set; }

        public string ConnectionId { get; } = connectionId;
        public int? UserId { get; set; }
        public Guid? UserSessionId { get; set; }
        public string IpEndpoint { get; } = ip;
        public bool IsAvailable => CloseReason == null;
        public bool IsClosed => CloseReason != null;
        public CancellationTokenSource TokenSource { get; set; } = new();
        public long LastHeartbeat { get; set; }
        public ConnectionRequestGuard RequestGuard { get; } = new(new FakeTimeProvider());
        public ConnectionHandlingQueue HandlingQueue { get; } = new();

        public void Send(BaseMessage message) => Sent.Add(message);
        public void SendSerialized(byte[] utf8Json) { }

        public void Close(ConnectionCloseReason reason = ConnectionCloseReason.Normal)
        {
            CloseReason = reason;
            Disconnected?.Invoke(ConnectionId);
        }

        public void Dispose() => TokenSource.Dispose();
    }
}
