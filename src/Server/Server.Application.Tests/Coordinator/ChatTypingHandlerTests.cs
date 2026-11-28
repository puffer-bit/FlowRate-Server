using Moq;
using Server.Application.Coordinator.Connections.RateLimiting;
using Server.Application.Coordinator.Handlers.Chat;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Roles;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Users;
using Server.Core.Models;
using Server.Core.Models.TextChat;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Notifications.SessionInfo;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Models.Responses;
using Shared.Models.Responses.SessionActions.ChatSession;

namespace Server.Application.Tests.Coordinator;

public sealed class ChatTypingHandlerTests
{
    private readonly Mock<IChatSessionManager> _chats = new();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<IMessageSender> _sender = new();
    private readonly Mock<ICoordinatorPermissions> _permissions = new();
    private readonly ChatTypingHandler _handler;

    public ChatTypingHandlerTests()
    {
        var chat = ChatSessionModel.Create("coordinator", "general", 1);
        chat.Id = 7;
        _chats.Setup(c => c.TryGetSession(7, out chat)).Returns(true);
        UserModel? user = null;
        _users.Setup(u => u.TryGetConnectedUser(9, out user)).Returns(true);
        _users.Setup(u => u.GetAllConnectedUsers()).Returns(new Dictionary<int, UserModel>
            { [9] = null!, [10] = null!, [11] = null! });
        var sessions = new Mock<ISessionManager>();
        SessionModel model = chat;
        sessions.Setup(s => s.TryGetSessionModel(7, out model)).Returns(true);
        _permissions.Setup(p => p.HasIn(It.IsAny<int>(), chat, Permission.ViewSessions))
            .Returns((int id, SessionModel _, Permission _) => id != 11);
        var visibility = new SessionVisibility(_sender.Object);
        visibility.Watch(_permissions.Object, sessions.Object, _users.Object);
        _handler = new ChatTypingHandler(_chats.Object, _users.Object, visibility, _sender.Object);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActivityGoesOnlyToViewersAndUsesTheAuthenticatedSender(bool active)
    {
        var request = new ChatTypingRequest(7, active) { SenderId = 9 };
        await _handler.HandleMessage(request, "connection");
        _sender.Verify(s => s.BroadcastMessage(
            It.Is<IEnumerable<int>>(ids => ids.Order().SequenceEqual(new[] { 9, 10 })),
            It.Is<ChatSessionUserTypingNotification>(n => n.ChatId == 7 && n.UserId == 9 && n.IsTyping == active)),
            Times.Once);
        _sender.Verify(s => s.SendMessageById("connection",
            It.Is<ChatTypingResponse>(r => r.RequestId == request.RequestId)), Times.Once);
    }

    [Theory]
    [InlineData(7, 99, ErrorCode.NotConnected)]
    [InlineData(99, 9, ErrorCode.ChatNotFound)]
    public async Task InvalidActivityDoesNotBroadcast(int chatId, int userId, ErrorCode error)
    {
        var request = new ChatTypingRequest(chatId, true) { SenderId = userId };
        await _handler.HandleMessage(request, "connection");
        _sender.Verify(s => s.BroadcastMessage(It.IsAny<IEnumerable<int>>(), It.IsAny<BaseMessage>()), Times.Never);
        _sender.Verify(s => s.SendMessageById("connection",
            It.Is<ErrorResponse>(r => r.RequestId == request.RequestId && r.Code == error)), Times.Once);
    }

    [Fact]
    public void ActivityRequiresWriteAccessAndIsRefusedForFrozenUsers()
    {
        var request = new ChatTypingRequest(7, true);
        Assert.Equal(RequestPolicy.InSession(7, Permission.ViewSessions | Permission.SendMessages), RequestPolicies.Of(request));
        Assert.False(RequestPolicies.IsAllowedWhenFrozen(request));
        Assert.Equal(RequestRateGroup.ChatWrite, RequestRateLimits.GroupOf(request));
    }
}
