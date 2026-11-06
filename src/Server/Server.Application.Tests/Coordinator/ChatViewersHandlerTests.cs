using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Connections.RateLimiting;
using Server.Application.Coordinator.Handlers.Chat;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Roles;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Users;
using Server.Core.Models.Roles;
using Server.Core.Models.TextChat;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Notifications.SessionInfo;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Models.Responses;
using Shared.Models.Responses.SessionActions.ChatSession;
using Shared.Permissions;

namespace Server.Application.Tests.Coordinator;

public sealed class ChatViewersHandlerTests : IDisposable
{
    private readonly Mock<IChatSessionManager> _chats = new();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<ICoordinatorPermissions> _permissions = new();
    private readonly Mock<IMessageSender> _sender = new();
    private readonly Mock<IConnectionManager> _connections = new();
    private readonly Dictionary<int, UserModel> _all = new();
    private readonly List<BaseMessage> _sent = [];
    private readonly ChatSessionModel _chat = ChatSessionModel.Create("coordinator", "general", 1);
    private readonly ChatViewersHandler _handler;
    private readonly SessionVisibility _visibility;

    public ChatViewersHandlerTests()
    {
        _chat.Id = 7;
        var chat = _chat;
        _chats.Setup(c => c.TryGetSession(7, out chat)).Returns(true);
        _permissions.Setup(p => p.HasIn(1, _chat, Permission.ViewSessions)).Returns(true);
        _permissions.SetupGet(p => p.Roles).Returns(new RoleSet([
            new RoleModel(1, "coordinator", "@everyone", null, 0, Permission.ViewSessions, true, 0),
            new RoleModel(2, "coordinator", "hidden", null, 1, Permission.None, false, 0)]));
        _chat.SetPermissionOverrides([new PermissionOverride(2, Permission.None, Permission.ViewSessions)]);
        _users.Setup(u => u.GetAllUsersAsync()).Returns(() =>
            Task.FromResult<IReadOnlyDictionary<int, UserModel>>(_all));
        _sender.Setup(s => s.SendMessageById("device", It.IsAny<BaseMessage>()))
            .Callback<string, BaseMessage>((_, message) => _sent.Add(message));
        _visibility = new SessionVisibility(_sender.Object);
        _handler = new ChatViewersHandler(_chats.Object, _users.Object, _permissions.Object,
            _sender.Object, _connections.Object, _visibility, NullLogger.Instance);
    }

    private static UserModel User(int id, string name, UserState state = UserState.Normal,
        params int[] roles) => new(id, "coordinator", name, state, UserStatus.Online,
            DateTime.UtcNow, DateTime.UtcNow, ip: "private-ip", roleIds: roles);

    private Task<HandleMessageResult> Subscribe(Guid? id = null, bool active = true) =>
        _handler.HandleMessage(new ChatViewersRequest(7, id ?? Guid.NewGuid(), active) { SenderId = 1 }, "device");

    [Fact]
    public async Task SnapshotIncludesOfflineAndBannedViewersAndFiltersHiddenAndSystemUsers()
    {
        _all[3] = User(3, "offline");
        _all[4] = User(4, "hidden", UserState.Normal, 2);
        _all[5] = User(5, "banned", UserState.Banned);
        _all[-1] = User(-1, "system", UserState.System);
        await Subscribe();
        var snapshot = Assert.IsType<ChatViewersResponse>(Assert.Single(_sent));
        Assert.Equal(new[] { 5, 3 }, snapshot.Users.Select(user => user.Id));
        Assert.Equal(UserState.Banned, snapshot.Users[0].State);
        var user = snapshot.Users[1];
        Assert.Equal(3, user.Id);
        Assert.Equal(UserStatus.Offline, user.Status);
        Assert.Null(user.Ip);
    }

    [Fact]
    public async Task ChangesSendOnlyUpsertsAndRemovalsWithoutAnotherSnapshot()
    {
        _all[3] = User(3, "Alice");
        _all[4] = User(4, "Bob");
        var id = Guid.NewGuid();
        await Subscribe(id);
        _sent.Clear();
        _users.Raise(u => u.UserUpdated += null, _all[3]);
        Assert.Empty(_sent);

        _all[3].Rename("Alicia");
        _all.Remove(4);
        _all[5] = User(5, "Carol");
        _users.Raise(u => u.UserUpdated += null, _all[3]);
        var delta = Assert.IsType<ChatViewersChangedNotification>(Assert.Single(_sent));
        Assert.Equal(id, delta.SubscriptionId);
        Assert.Equal(new[] { 3, 5 }, delta.Upserted.Select(u => u.Id));
        Assert.Equal(new[] { 4 }, delta.Removed);
    }

    [Fact]
    public async Task BanningAndUnbanningUpdateTheRowWithoutRemovingIt()
    {
        _all[3] = User(3, "Alice");
        await Subscribe();
        _sent.Clear();
        _all[3].Ban();
        _users.Raise(u => u.UserUpdated += null, _all[3]);
        var banned = Assert.IsType<ChatViewersChangedNotification>(Assert.Single(_sent));
        Assert.Empty(banned.Removed);
        Assert.Equal(UserState.Banned, Assert.Single(banned.Upserted).State);
        _sent.Clear();
        _all[3].Reinstate();
        _users.Raise(u => u.UserUpdated += null, _all[3]);
        var unbanned = Assert.IsType<ChatViewersChangedNotification>(Assert.Single(_sent));
        Assert.Empty(unbanned.Removed);
        Assert.Equal(UserState.Normal, Assert.Single(unbanned.Upserted).State);
    }

    [Fact]
    public async Task OfflineRoleAndChatOverrideChangesSendDiffs()
    {
        _all[3] = User(3, "Alice", UserState.Normal, 2);
        await Subscribe();
        Assert.Empty(Assert.IsType<ChatViewersResponse>(Assert.Single(_sent)).Users);
        _sent.Clear();
        _chat.SetPermissionOverrides([]);
        _visibility.Reconfigured(_chat);
        Assert.Equal(3, Assert.Single(Assert.IsType<ChatViewersChangedNotification>(Assert.Single(_sent)).Upserted).Id);
        _sent.Clear();
        _chat.SetPermissionOverrides([new PermissionOverride(2, Permission.None, Permission.ViewSessions)]);
        _permissions.Raise(p => p.PermissionsChanged += null, Array.Empty<int>());
        Assert.Equal(new[] { 3 }, Assert.IsType<ChatViewersChangedNotification>(Assert.Single(_sent)).Removed);
    }

    [Fact]
    public async Task ClosingAndDisconnectingStopTheSubscriptionAndStaleCloseDoesNotCloseANewOne()
    {
        var oldId = Guid.NewGuid();
        var id = Guid.NewGuid();
        await Subscribe(oldId);
        await Subscribe(id);
        await Subscribe(oldId, false);
        _sent.Clear();
        _all[3] = User(3, "Alice");
        _users.Raise(u => u.UserConnected += null, _all[3]);
        Assert.Equal(id, Assert.IsType<ChatViewersChangedNotification>(Assert.Single(_sent)).SubscriptionId);
        await Subscribe(id, false);
        _sent.Clear();
        _all[4] = User(4, "Bob");
        _users.Raise(u => u.UserConnected += null, _all[4]);
        Assert.Empty(_sent);
        await Subscribe();
        _connections.Raise(c => c.ConnectionClosed += null, "device");
        _sent.Clear();
        _all[5] = User(5, "Carol");
        _users.Raise(u => u.UserConnected += null, _all[5]);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task RevokedAccessClearsRowsAndStopsFurtherUpdates()
    {
        _all[3] = User(3, "Alice");
        await Subscribe();
        _sent.Clear();
        _permissions.Setup(p => p.HasIn(1, _chat, Permission.ViewSessions)).Returns(false);
        _permissions.Raise(p => p.PermissionsChanged += null, new[] { 1 });
        var delta = Assert.IsType<ChatViewersChangedNotification>(Assert.Single(_sent));
        Assert.Empty(delta.Upserted);
        Assert.Equal(new[] { 3 }, delta.Removed);
        _sent.Clear();
        _users.Raise(u => u.UserUpdated += null, _all[3]);
        Assert.Empty(_sent);
        await Subscribe();
        Assert.Equal(ErrorCode.ChatNotFound, Assert.IsType<ErrorResponse>(Assert.Single(_sent)).Code);
    }

    [Fact]
    public async Task DeletingTheObserverClearsAndClosesTheirSubscription()
    {
        _all[3] = User(3, "Alice");
        await Subscribe();
        _sent.Clear();
        _users.Raise(u => u.UserDeleted += null, 1);
        Assert.Equal(new[] { 3 }, Assert.IsType<ChatViewersChangedNotification>(Assert.Single(_sent)).Removed);
        _sent.Clear();
        _all[4] = User(4, "Bob");
        _users.Raise(u => u.UserConnected += null, _all[4]);
        Assert.Empty(_sent);
    }

    [Fact]
    public void PolicyAllowsReadingAndUnsubscriptionAfterAccessLoss()
    {
        Assert.Equal(RequestPolicy.InSession(7, Permission.ViewSessions),
            RequestPolicies.Of(new ChatViewersRequest(7, Guid.NewGuid())));
        Assert.Equal(RequestPolicy.Open, RequestPolicies.Of(new ChatViewersRequest(7, Guid.NewGuid(), false)));
        Assert.True(RequestPolicies.IsAllowedWhenFrozen(new ChatViewersRequest(7, Guid.NewGuid())));
        Assert.Equal(RequestRateGroup.ChatRead, RequestRateLimits.GroupOf(new ChatViewersRequest(7, Guid.NewGuid())));
    }

    public void Dispose() { _handler.Dispose(); _visibility.Dispose(); }
}
