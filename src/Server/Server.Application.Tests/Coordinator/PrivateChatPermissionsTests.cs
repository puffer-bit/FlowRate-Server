using Microsoft.Extensions.Logging.Abstractions;
using Server.Application.Abstractions.Media;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Media;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Core.Models.Coordinator.Settings;
using Server.Core.Models.Media;
using Moq;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Roles;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Users;
using Server.Application.Orchestrator.Roles;
using Server.Core.Models;
using Server.Core.Models.TextChat;
using Shared.Enums;
using Shared.Models.Requests.SessionActions.ChatSession;
using Xunit;

namespace Server.Application.Tests.Coordinator;

public sealed class PrivateChatPermissionsTests
{
    [Theory]
    [InlineData(ChatKind.Direct)]
    [InlineData(ChatKind.Group)]
    public async Task Membership_DecidesReadWriteAndManagement_OnlineAndOffline(ChatKind kind)
    {
        var chat = ChatSessionModel.Create("server", "Private", 1);
        chat.SetAudience(kind, [1, 2]);
        var permissions = new CoordinatorRoles(NullLogger.Instance, Mock.Of<IRoleService>(), Mock.Of<IUserManager>(),
            Mock.Of<IMessageSender>(), "server");
        const Permission write = Permission.ViewSessions | Permission.SendMessages | Permission.AttachFiles;
        Assert.True(permissions.HasIn(2, chat, write));
        Assert.Equal(Permission.None, permissions.PermissionsIn(99, chat));
        Assert.Equal(Permission.None, await permissions.PermissionsInAsync(99, chat));
        Assert.False(permissions.HasIn(2, chat, Permission.ManageSessions));
        Assert.Equal(kind == ChatKind.Group, permissions.HasIn(1, chat, Permission.ManageSessions));
        chat.SetAudience(kind, kind == ChatKind.Group ? [1] : [1, 3]);
        Assert.False(permissions.HasIn(2, chat, Permission.ViewSessions));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(99)]
    public async Task AttachmentDownload_RefusesNonmembersBeforeOpeningStorage(int? userId)
    {
        var chat = ChatSessionModel.Create("server", "Private", 1);
        chat.Id = 5;
        chat.SetAudience(ChatKind.Direct, [1, 2]);
        var chats = new Mock<IChatSessionManager>();
        ChatSessionModel? found = chat;
        chats.Setup(manager => manager.TryGetSession(5, out found)).Returns(true);
        var uploads = new Mock<IMediaUploadRepository>();
        uploads.Setup(repository => repository.GetImageAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatImageFileModel(10, 5, false, "image/png", "original", "preview"));
        uploads.Setup(repository => repository.GetFileAsync(11, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatFileDownloadModel(11, 5, false, "private.txt", "file"));
        var storage = new Mock<IMediaStorage>(MockBehavior.Strict);
        var service = new MediaService(NullLogger.Instance, uploads.Object, storage.Object, chats.Object,
            CoordinatorUploadSettings.Default, CoordinatorMediaSettings.Default, "server");
        Assert.Null(await service.OpenImageAsync(10, false, requestingUserId: userId));
        Assert.Null(await service.OpenFileAsync(11, requestingUserId: userId));
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ServerAdministrator_OutsidePrivateChat_IsRefusedEvenWithAllRolePermissions()
    {
        var chat = ChatSessionModel.Create("server", "Private", 1);
        chat.Id = 5;
        chat.SetAudience(ChatKind.Direct, [1, 2]);
        var sessions = new Mock<ISessionManager>();
        SessionModel? found = chat;
        sessions.Setup(manager => manager.TryGetSessionModel(5, out found)).Returns(true);
        var permissions = new Mock<ICoordinatorPermissions>();
        permissions.Setup(p => p.Has(It.IsAny<int>(), It.IsAny<Permission>())).Returns(true);
        permissions.Setup(p => p.HasIn(It.IsAny<int>(), chat, It.IsAny<Permission>())).Returns(true);
        var authorizer = new RequestAuthorizer(permissions.Object, Mock.Of<IHybridSessionManager>(), sessions.Object);
        Assert.False(await authorizer.IsAllowedAsync(new ChatSessionHistoryRequest(5, 10, 0) { SenderId = 99 }));
        Assert.True(await authorizer.IsAllowedAsync(new ChatSessionHistoryRequest(5, 10, 0) { SenderId = 2 }));
    }
}
