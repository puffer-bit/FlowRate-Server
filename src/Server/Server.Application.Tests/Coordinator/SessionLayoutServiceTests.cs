using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Layout;
using Server.Core.Models.Layout;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Notifications.SessionInfo;
using Shared.Validation;

namespace Server.Application.Tests.Coordinator;

public class SessionLayoutServiceTests
{
    private readonly Mock<ISessionLayoutRepository> _repository = new();
    private readonly List<BaseMessage> _broadcast = [];
    private readonly SessionLayoutService _service;
    private int _nextCategoryId = 100;

    public SessionLayoutServiceTests()
    {
        _repository.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionLayoutModel([2], [(new SessionCategoryModel(7, "Voice"), [3])]));
        _repository.Setup(r => r.AddCategoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _nextCategoryId++);

        var sender = new Mock<IMessageSender>();
        sender.Setup(s => s.BroadcastMessage(It.IsAny<BaseMessage>())).Callback<BaseMessage>(_broadcast.Add);

        _service = new SessionLayoutService(NullLogger.Instance, _repository.Object, sender.Object, "coordinator");
    }

    [Fact]
    public async Task Load_PlacesUnknownSessions_AndForgetsGoneOnes()
    {
        await _service.LoadAsync([1, 2, 4]);

        var layout = _service.GetLayout();
        Assert.Equal([2, 1, 4], layout.RootSessionIds);
        Assert.Empty(Assert.Single(layout.Categories).SessionIds);
        _repository.Verify(r => r.SaveAsync("coordinator", It.IsAny<SessionLayoutModel>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Move_SavesAndBroadcastsANewerRevision()
    {
        await _service.LoadAsync([2, 3]);
        var before = _service.GetLayout().Revision;

        Assert.Equal(SessionLayoutResult.NoError, await _service.MoveSessionAsync(2, 7, 0));

        var notification = Assert.IsType<SessionLayoutChangedNotification>(Assert.Single(_broadcast));
        Assert.True(notification.Layout.Revision > before);
        Assert.Equal([2, 3], Assert.Single(notification.Layout.Categories).SessionIds);
    }

    [Fact]
    public async Task MoveToTheSamePlace_BroadcastsNothing()
    {
        await _service.LoadAsync([2, 3]);

        Assert.Equal(SessionLayoutResult.NoError, await _service.MoveSessionAsync(2, null, 0));

        Assert.Empty(_broadcast);
    }

    [Fact]
    public async Task FailedSave_KeepsTheOldLayout()
    {
        await _service.LoadAsync([2, 3]);
        _repository.Setup(r => r.SaveAsync(It.IsAny<string>(), It.IsAny<SessionLayoutModel>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());

        Assert.Equal(SessionLayoutResult.InternalError, await _service.MoveSessionAsync(2, 7, 0));

        Assert.Equal([2], _service.GetLayout().RootSessionIds);
        Assert.Empty(_broadcast);
    }

    [Fact]
    public async Task Unknown_IsReported()
    {
        await _service.LoadAsync([2, 3]);

        Assert.Equal(SessionLayoutResult.SessionNotExists, await _service.MoveSessionAsync(9, null, 0));
        Assert.Equal(SessionLayoutResult.CategoryNotExists, await _service.MoveSessionAsync(2, 9, 0));
        Assert.Equal(SessionLayoutResult.CategoryNotExists, await _service.DeleteCategoryAsync(9));
    }

    [Fact]
    public async Task CreateCategory_AddsItAtTheEnd()
    {
        await _service.LoadAsync([2, 3]);

        var (result, categoryId) = await _service.CreateCategoryAsync("Games");

        Assert.Equal(SessionLayoutResult.NoError, result);
        Assert.Equal([7, categoryId!.Value], _service.GetLayout().Categories.Select(c => c.Id));
    }

    [Fact]
    public async Task CreateCategory_StopsAtTheLimit()
    {
        await _service.LoadAsync([2, 3]);
        for (var i = 1; i < Limits.SessionCategoriesMaxCount; i++)
            await _service.CreateCategoryAsync("c" + i);

        Assert.Equal(SessionLayoutResult.TooManyCategories, (await _service.CreateCategoryAsync("one more")).Result);
    }

    [Fact]
    public async Task DeleteCategory_MovesItsSessionsToRoot()
    {
        await _service.LoadAsync([2, 3]);

        await _service.DeleteCategoryAsync(7);

        var layout = _service.GetLayout();
        Assert.Equal([2, 3], layout.RootSessionIds);
        Assert.Empty(layout.Categories);
    }
}
