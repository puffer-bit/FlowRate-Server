using Moq;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.SessionActions.Generic;
using Shared.Models.Responses.SessionActions.Generic;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Handlers.Hybrid;
using Shared.Models.Requests.SessionActions.HybridSession;

namespace Server.Application.Tests.Coordinator;

public class HybridSessionRequestHandlerTests
{
    private const int SessionId = 7;
    private const int HostId = 1;
    private const int GuestId = 2;

    private readonly Mock<IHybridSessionManager> _sessions = new();
    private readonly Mock<IHybridSessionPeerManager> _peers = new();
    private readonly List<BaseMessage> _sent = [];
    private readonly KickFromSessionHandler _handler;
    private readonly LeaveSessionHandler _leave;
    private readonly HybridSessionJoinHandler _join;

    public HybridSessionRequestHandlerTests()
    {
        var session = new Mock<IHybridSession>();
        session.Setup(s => s.PeerManager).Returns(_peers.Object);
        var found = session.Object;
        _sessions.Setup(m => m.TryGetSession(SessionId, out found)).Returns(true);

        var sender = new Mock<IMessageSender>();
        sender.Setup(s => s.SendMessageById(It.IsAny<string>(), It.IsAny<BaseMessage>()))
            .Callback((string _, BaseMessage message) => _sent.Add(message));

        _handler = new KickFromSessionHandler(_sessions.Object, sender.Object);
        _leave = new LeaveSessionHandler(_sessions.Object, sender.Object);
        _join = new HybridSessionJoinHandler(_sessions.Object, sender.Object);
    }

    private static KickFromSessionRequest Kick(int sessionId, SessionType type = SessionType.Hybrid) =>
        new(sessionId, GuestId, type, "bye") { SenderId = HostId };

    [Fact]
    public async Task Kick_KicksTheTargetOnBehalfOfTheSender()
    {
        _peers.Setup(p => p.KickPeerAsync(HostId, GuestId, "bye")).ReturnsAsync(LeaveSessionResult.NoError);

        await _handler.HandleMessage(Kick(SessionId), "connection");

        _peers.Verify(p => p.KickPeerAsync(HostId, GuestId, "bye"), Times.Once);
        var response = Assert.IsType<KickFromSessionResponse>(Assert.Single(_sent));
        Assert.Equal(LeaveSessionResult.NoError, response.Result);
    }

    [Fact]
    public async Task UnknownHybridSession_AnswersSessionNotExists()
    {
        await _handler.HandleMessage(Kick(sessionId: 99), "connection");

        var response = Assert.IsType<KickFromSessionResponse>(Assert.Single(_sent));
        Assert.Equal(LeaveSessionResult.SessionNotExists, response.Result);
    }

    [Fact]
    public async Task OtherSessionType_AnswersUnknownSessionType()
    {
        await _handler.HandleMessage(Kick(sessionId: 99, SessionType.Chat), "connection");

        var response = Assert.IsType<KickFromSessionResponse>(Assert.Single(_sent));
        Assert.Equal(LeaveSessionResult.UnknownSessionType, response.Result);
    }

    [Fact]
    public async Task NullRequest_IsForbiddenAndNotAnswered()
    {
        Assert.Equal(HandleMessageResult.ForbiddenRequest, await _handler.HandleMessage(null, "connection"));
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task Leave_FromAnotherDeviceThanThePeerIsOn_LeavesThePeerAlone()
    {
        _peers.Setup(p => p.IsPeerOf(GuestId, "phone")).Returns(false);

        await _leave.HandleMessage(new LeaveSessionRequest(SessionId, SessionType.Hybrid) { SenderId = GuestId }, "phone");

        _peers.Verify(p => p.RemovePeerAsync(It.IsAny<int>()), Times.Never);
        var response = Assert.IsType<LeaveSessionResponse>(Assert.Single(_sent));
        Assert.Equal(LeaveSessionResult.InternalError, response.Result);
    }

    [Fact]
    public async Task Leave_FromTheDeviceThePeerIsOn_LeavesTheSession()
    {
        _peers.Setup(p => p.IsPeerOf(GuestId, "desktop")).Returns(true);
        _peers.Setup(p => p.RemovePeerAsync(GuestId)).ReturnsAsync(LeaveSessionResult.NoError);

        await _leave.HandleMessage(new LeaveSessionRequest(SessionId, SessionType.Hybrid) { SenderId = GuestId }, "desktop");

        var response = Assert.IsType<LeaveSessionResponse>(Assert.Single(_sent));
        Assert.Equal(LeaveSessionResult.NoError, response.Result);
    }

    [Fact]
    public async Task Join_TakesPartFromTheConnectionOfTheRequest()
    {
        _peers.Setup(p => p.AddPeerAsync(GuestId, It.IsAny<string>(), "desktop"))
            .ReturnsAsync((HybridSessionJoinResult.NoError, null));

        await _join.HandleMessage(new HybridSessionJoinRequest(SessionId) { SenderId = GuestId }, "desktop");

        _peers.Verify(p => p.AddPeerAsync(GuestId, It.IsAny<string>(), "desktop"), Times.Once);
    }
}
