using Shared.Permissions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Tests.Fakes;
using Server.Core.Models.TextChat;
using Shared.Enums;
using Shared.Models.Requests.SessionActions.Generic.Models;
using Shared.Models.Requests.SessionActions.Generic.Models.SessionData;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Layout;
using Server.Application.Coordinator.Messaging;
using Server.Core.Models.Layout;

namespace Server.Application.Tests.Coordinator;

public class SessionManagerTests
{
    private const int UserId = 1;

    private readonly ChatSessionManager _chatSessions;
    private readonly Mock<IHybridSessionManager> _hybridSessions = new();
    private readonly SessionLayoutService _layout;
    private readonly SessionManager _manager;

    public SessionManagerTests()
    {
        var chats = new Mock<IChatSessionRepository>();
        chats.Setup(r => r.AddAsync(It.IsAny<ChatSessionModel>(), It.IsAny<CancellationToken>())).ReturnsAsync(10);

        _chatSessions = new ChatSessionManager(NullLogger.Instance, new RecordingEventBus(), chats.Object,
            new Mock<IMessagesRepository>().Object,
            new Mock<IMediaUploadRepository>().Object, new Mock<Server.Application.Abstractions.Media.IMediaStorage>().Object,
            "coordinator");
        var layoutRepository = new Mock<ISessionLayoutRepository>();
        layoutRepository.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionLayoutModel());
        _layout = new SessionLayoutService(NullLogger.Instance, layoutRepository.Object,
            new Mock<IMessageSender>().Object, "coordinator");
        var permissions = new Mock<ISessionPermissionsRepository>();
        permissions.Setup(r => r.GetByCoordinatorAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, IReadOnlyList<PermissionOverride>>());
        _manager = new SessionManager(_chatSessions, _hybridSessions.Object, _layout, permissions.Object, "coordinator");
    }

    private Task<(ErrorCode? result, Core.Models.SessionModel? session)> CreateChat(int? categoryId = null) =>
        _manager.CreateSessionAsync(new CreateSessionModel("general", 10, new ChatSessionCreateData(), categoryId), UserId);

    [Fact]
    public async Task CreateSession_PlacesTheSessionInTheRequestedCategory()
    {
        var (_, categoryId) = await CreateCategory();

        await CreateChat(categoryId);

        var layout = _layout.GetLayout();
        Assert.Empty(layout.RootSessionIds);
        Assert.Equal([10], Assert.Single(layout.Categories).SessionIds);
    }

    [Fact]
    public async Task DeleteSession_TakesItOutOfTheLayout()
    {
        await CreateChat();

        await _manager.DeleteSessionAsync(10, UserId);

        Assert.Empty(_layout.GetLayout().RootSessionIds);
    }

    private Task<(ErrorCode? Result, int? CategoryId)> CreateCategory() =>
        _layout.CreateCategoryAsync("Voice");

    [Fact]
    public async Task CreateSession_Chat_ReturnsTheCreatedChat()
    {
        var (result, session) = await CreateChat();

        Assert.Equal(null, result);
        var chat = Assert.IsType<ChatSessionModel>(session);
        Assert.Equal("general", chat.Name);
    }

    [Fact]
    public async Task TryGetSessionModel_FindsChats()
    {
        await CreateChat();

        Assert.True(_manager.TryGetSessionModel(10, out var session));
        Assert.Equal(SessionType.Chat, session.SessionType);
    }

    [Fact]
    public async Task DeleteSession_Chat_DeletesIt()
    {
        await CreateChat();

        Assert.Equal(null, await _manager.DeleteSessionAsync(10, UserId));

        Assert.False(_manager.TryGetSessionModel(10, out _));
        _hybridSessions.Verify(m => m.DeleteAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DeleteSession_Unknown_ReturnsSessionNotExists()
    {
        Assert.Equal(ErrorCode.SessionNotFound, await _manager.DeleteSessionAsync(99, UserId));
    }

    [Fact]
    public async Task GetAllSessionModels_IncludesChats()
    {
        _hybridSessions.Setup(m => m.GetAllSessions()).Returns([]);
        await CreateChat();

        var session = Assert.Single(_manager.GetAllSessionModels());
        Assert.Equal(10, session.Id);
    }
}
