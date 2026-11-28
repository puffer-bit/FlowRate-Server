using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Abstractions.Media;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Media;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Roles;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Users;
using Server.Application.Orchestrator.Roles;
using Server.Core.Models;
using Server.Core.Models.Coordinator.Settings;
using Server.Core.Models.HybridSession;
using Server.Core.Models.Media;
using Server.Core.Models.Roles;
using Server.Core.Models.TextChat;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Enums.WebRTC;
using Shared.Models;
using Shared.Models.Notifications.CoordinatorInfo;
using Shared.Models.Notifications.SessionInfo;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Models.Requests.SessionActions.HybridSession;
using Shared.Permissions;

namespace Server.Application.Tests.Coordinator;

public sealed class ServerChannelAccessTests
{
    private const string Coordinator = "server";
    private readonly Mock<IRoleService> _service = new();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<IMessageSender> _sender = new();
    private RoleSet _roles = Roles(Permission.None);
    private readonly UserModel _user = new(7, Coordinator, "Member", UserState.Normal, UserStatus.Online,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, roleIds: [2]);

    private static RoleSet Roles(Permission member) => new([
        new RoleModel(1, Coordinator, "@everyone", null, 0, Permission.Default & ~Permission.ViewSessions, true, 0),
        new RoleModel(2, Coordinator, "Member", null, 1, member, false, 0)]);

    private CoordinatorRoles CreatePermissions(bool connected = true)
    {
        _service.Setup(s => s.GetLoaded(Coordinator)).Returns(() => _roles);
        UserModel? user = connected ? _user : null;
        _users.Setup(u => u.TryGetConnectedUser(7, out user)).Returns(connected);
        _users.Setup(u => u.GetUserAsync(7)).ReturnsAsync(_user);
        _users.Setup(u => u.GetAllConnectedUsers()).Returns(new Dictionary<int, UserModel> { [7] = _user });
        return new CoordinatorRoles(NullLogger.Instance, _service.Object, _users.Object, _sender.Object, Coordinator);
    }

    private static SessionModel Channel(bool voice)
    {
        SessionModel channel = voice
            ? new HybridSessionModel(10, Coordinator, "Voice", 10, WebRTCEngine.Mediasoup)
            : ChatSessionModel.Create(Coordinator, "Text", 7);
        channel.Id = 10;
        channel.SetPermissionOverrides([new PermissionOverride(1, Permission.ViewSessions, Permission.None)]);
        return channel;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ChannelOverrideCannotRestoreMissingGlobalView(bool voice, bool connected)
    {
        using var permissions = CreatePermissions(connected);
        var channel = Channel(voice);

        Assert.False(permissions.Has(7, Permission.ViewSessions));
        Assert.Equal(Permission.None, permissions.PermissionsIn(7, channel));
        Assert.Equal(Permission.None, await permissions.PermissionsInAsync(7, channel));
        Assert.Equal(Permission.All, permissions.PermissionsIn(UserModel.SystemUserId, channel));
    }

    [Theory]
    [InlineData(Permission.ViewSessions)]
    [InlineData(Permission.Administrator)]
    public async Task AnotherRoleCanGrantAccess(Permission grant)
    {
        _roles = Roles(grant);
        using var permissions = CreatePermissions();
        Assert.True(permissions.HasIn(7, Channel(false), Permission.ViewSessions));
        Assert.True((await permissions.PermissionsInAsync(7, Channel(true))).HasFlag(Permission.ViewSessions));
    }

    [Theory]
    [InlineData(ChatKind.Direct)]
    [InlineData(ChatKind.Group)]
    public async Task PrivateMessagesAndCallsRemainAvailableWithoutChannelAccess(ChatKind kind)
    {
        using var permissions = CreatePermissions();
        var chat = ChatSessionModel.Create(Coordinator, "Private", 7);
        chat.Id = 11;
        chat.SetAudience(kind, [7, 8]);
        var call = new HybridSessionModel(11, Coordinator, "Call", 10, WebRTCEngine.Mediasoup)
        {
            CallDevices = new Dictionary<int, string> { [7] = "device" }
        };
        const Permission write = Permission.ViewSessions | Permission.SendMessages | Permission.AttachFiles;
        const Permission speak = Permission.ViewSessions | Permission.JoinSessions | Permission.Speak | Permission.ShareVideo;

        Assert.False(permissions.Has(7, Permission.ViewSessions));
        Assert.True(permissions.HasIn(7, chat, write));
        Assert.True((await permissions.PermissionsInAsync(7, chat)).HasFlag(write));
        Assert.True(permissions.HasIn(7, call, speak));
        Assert.True((await permissions.PermissionsInAsync(7, call)).HasFlag(speak));
        Assert.Equal(Permission.None, permissions.PermissionsIn(99, chat));
        Assert.Equal(Permission.None, permissions.PermissionsIn(99, call));

        var sessions = new Mock<ISessionManager>();
        SessionModel? model = chat;
        sessions.Setup(s => s.TryGetSessionModel(11, out model)).Returns(true);
        var authorizer = new RequestAuthorizer(permissions, Mock.Of<IHybridSessionManager>(), sessions.Object);
        Assert.True(await authorizer.IsAllowedAsync(new ChatSessionHistoryPageRequest(11, null, 10) { SenderId = 7 }));
        Assert.True(await authorizer.IsAllowedAsync(new ChatTypingRequest(11, true) { SenderId = 7 }));
    }

    [Fact]
    public async Task ChannelAttachmentsAreDeniedBeforeStorageIsOpened()
    {
        using var permissions = CreatePermissions(connected: false);
        var chat = (ChatSessionModel)Channel(false);
        var chats = new Mock<IChatSessionManager>();
        ChatSessionModel? found = chat;
        chats.Setup(c => c.TryGetSession(10, out found)).Returns(true);
        var uploads = new Mock<IMediaUploadRepository>();
        uploads.Setup(u => u.GetImageAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatImageFileModel(20, 10, false, "image/png", "image", "preview"));
        uploads.Setup(u => u.GetFileAsync(21, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatFileDownloadModel(21, 10, false, "file.txt", "file"));
        var storage = new Mock<IMediaStorage>(MockBehavior.Strict);
        var media = new MediaService(NullLogger.Instance, uploads.Object, storage.Object, chats.Object,
            CoordinatorUploadSettings.Default, CoordinatorMediaSettings.Default, Coordinator);
        media.Watch(permissions);

        Assert.Null(await media.OpenImageAsync(20, false, requestingUserId: 7));
        Assert.Null(await media.OpenFileAsync(21, requestingUserId: 7));
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public void EmptyChannelListDoesNotMeanMissingChannelPermission()
    {
        _roles = Roles(Permission.ViewSessions);
        using var permissions = CreatePermissions();
        using var visibility = new SessionVisibility(_sender.Object);
        var sessions = new Mock<ISessionManager>();
        sessions.Setup(s => s.GetAllSessionModels()).Returns([]);
        visibility.Watch(permissions, sessions.Object, _users.Object);

        Assert.Empty(visibility.ShowAll(7));
        Assert.True(permissions.Has(7, Permission.ViewSessions));

        var channel = Channel(false);
        channel.SetPermissionOverrides([new PermissionOverride(1, Permission.None, Permission.ViewSessions)]);
        sessions.Setup(s => s.GetAllSessionModels()).Returns([channel]);
        Assert.Empty(visibility.ShowAll(7));
        Assert.True(permissions.Has(7, Permission.ViewSessions));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectChannelRequestsAreDenied(bool voice)
    {
        using var permissions = CreatePermissions();
        var sessions = new Mock<ISessionManager>();
        SessionModel? model = Channel(voice);
        sessions.Setup(s => s.TryGetSessionModel(10, out model)).Returns(true);
        var authorizer = new RequestAuthorizer(permissions, Mock.Of<IHybridSessionManager>(), sessions.Object);
        BaseMessage request = voice ? new HybridSessionJoinRequest(10) : new ChatSessionHistoryPageRequest(10, null, 10);

        Assert.False(await authorizer.IsAllowedAsync(request with { SenderId = 7 }));
    }

    [Fact]
    public async Task RoleChangesHideAndRestoreChannelsWhilePrivateChatsStayVisible()
    {
        _roles = Roles(Permission.ViewSessions);
        using var permissions = CreatePermissions();
        await permissions.LoadAsync();
        var channel = Channel(false);
        var chat = ChatSessionModel.Create(Coordinator, "Group", 7);
        chat.Id = 11;
        chat.SetAudience(ChatKind.Group, [7, 8]);
        var sessions = new Mock<ISessionManager>();
        sessions.Setup(s => s.GetAllSessionModels()).Returns([channel, chat]);
        using var visibility = new SessionVisibility(_sender.Object);
        visibility.Watch(permissions, sessions.Object, _users.Object);
        Assert.Equal(2, visibility.ShowAll(7).Count);

        _roles = Roles(Permission.None);
        _service.Raise(s => s.RolesChanged += null, new RolesChange(Coordinator, _roles, true,
            new Dictionary<int, IReadOnlyList<int>>()));

        Assert.Same(chat, Assert.Single(visibility.ShowAll(7)));
        _sender.Verify(s => s.SendMessageByUserId(7, It.Is<ChatSessionDeletedNotification>(n => n.SessionId == 10)), Times.Once);
        _sender.Verify(s => s.SendMessageByUserId(7, It.Is<CoordinatorPermissionsChangedNotification>(n =>
            !n.Permissions.HasFlag(Permission.ViewSessions))), Times.Once);

        _roles = Roles(Permission.ViewSessions);
        _service.Raise(s => s.RolesChanged += null, new RolesChange(Coordinator, _roles, true,
            new Dictionary<int, IReadOnlyList<int>>()));

        Assert.Equal(2, visibility.ShowAll(7).Count);
        _sender.Verify(s => s.SendMessageByUserId(7, It.IsAny<ChatSessionCreatedNotification>()), Times.Once);
    }
}
