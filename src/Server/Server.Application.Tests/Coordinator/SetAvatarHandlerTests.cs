using Server.Application.Tests.Fakes;
using Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Handlers.Users;
using Server.Application.Coordinator.Media;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Users;
using Server.Core.Models.User;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Requests.Coordinator;
using Shared.Models.Responses.Coordinator;

namespace Server.Application.Tests.Coordinator;

public class SetAvatarHandlerTests
{
    private const string CoordinatorId = "coordinator";
    private const string Upload = "0123456789abcdef0123456789abcdef";
    private const string Previous = "fedcba9876543210fedcba9876543210";

    private readonly Mock<IMediaService> _media = new();
    private readonly Mock<IMessageSender> _sender = new();
    private readonly UserManagerContext _context = new();
    private readonly SetAvatarHandler _handler;
    private BaseMessage? _response;

    public SetAvatarHandlerTests()
    {
        var manager = new UserManager(_context, NullLogger.Instance, CoordinatorId, new Mock<IUserRepository>().Object);
        _handler = new SetAvatarHandler(NullLogger.Instance, _sender.Object, manager, _media.Object);
        _sender.Setup(s => s.SendMessageById(It.IsAny<string>(), It.IsAny<BaseMessage>()))
            .Callback((string _, BaseMessage message) => _response = message);
        _media.Setup(m => m.AttachAvatarAsync(Upload, 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private UserModel Attach(int id, string? avatarUrl = null)
    {
        var user = new UserModel(id, CoordinatorId, $"user{id}", UserState.Normal, UserStatus.Online,
            DateTime.UtcNow, DateTime.UtcNow, avatarUrl: avatarUrl);
        _context.AttachedUsers[id] = user;
        return user;
    }

    private async Task<BaseMessage> SendAsync(string? uploadId, int? userId = null, int senderId = 1)
    {
        await _handler.HandleMessage(new CoordinatorSetAvatarRequest(uploadId, userId) { SenderId = senderId }, "connection");
        return _response!;
    }

    [Fact]
    public async Task Upload_BecomesTheAvatar_AndThePreviousOneIsRemoved()
    {
        var user = Attach(1, AvatarAddress.Of(Previous));

        var response = await SendAsync(Upload);
        Assert.Equal(AvatarAddress.Of(Upload), user.AvatarUrl);
        Assert.Equal(user.AvatarUrl, Assert.IsType<CoordinatorSetAvatarResponse>(response).User.AvatarUrl);
        _media.Verify(m => m.RemoveAvatarAsync(Previous, It.IsAny<CancellationToken>()), Times.Once);
        _media.Verify(m => m.RemoveAvatarAsync(Upload, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Upload_ThatIsNotAFinishedAvatarOfTheSender_IsRefused()
    {
        var user = Attach(1, AvatarAddress.Of(Previous));
        _media.Setup(m => m.AttachAvatarAsync(Upload, 1, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var response = await SendAsync(Upload);

        Assert.Equal(ErrorCode.UploadNotFound, response.Error());
        Assert.Equal(AvatarAddress.Of(Previous), user.AvatarUrl);
        _media.Verify(m => m.RemoveAvatarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Picture_CannotBeSetForAnotherUser()
    {
        Attach(1);
        var other = Attach(2);

        var response = await SendAsync(Upload, userId: 2);

        Assert.Equal(ErrorCode.InsufficientPermissions, response.Error());
        Assert.Null(other.AvatarUrl);
        _media.Verify(m => m.AttachAvatarAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reset_TakesTheAvatarOffAnotherUser_AndRemovesItsFile()
    {
        Attach(1);
        var other = Attach(2, AvatarAddress.Of(Previous));

        var response = await SendAsync(null, userId: 2);
        Assert.Null(other.AvatarUrl);
        Assert.Null(Assert.IsType<CoordinatorSetAvatarResponse>(response).User.AvatarUrl);
        _media.Verify(m => m.RemoveAvatarAsync(Previous, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task User_NotConnected_IsNotFound()
    {
        Attach(1);

        var response = await SendAsync(null, userId: 9);

        Assert.Equal(ErrorCode.UserNotFound, response.Error());
    }
}
