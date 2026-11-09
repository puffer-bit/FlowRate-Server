using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Moderation;
using Server.Application.Coordinator.Roles;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Users;
using Server.Application.Orchestrator;
using Server.Application.Orchestrator.Settings;
using Server.Asp.Web.Api;
using Server.Core.Models.Coordinator;
using Server.Core.Models.Moderation;
using Server.Core.Models.User;
using Shared.Enums.User;

namespace Server.Asp.Tests.Web.Api;

public class BansControllerTests
{
    private readonly Mock<IOrchestratorInstance> _orchestrator = new();
    private readonly Mock<IBanRepository> _repository = new();
    private readonly Mock<ICoordinatorSettingsService> _settings = new();
    private readonly BansController _controller;
    private readonly CoordinatorInstanceModel _coordinator = CoordinatorInstanceModel.Create("test", "127.0.0.1", 5000, DateTime.UtcNow);

    public BansControllerTests()
    {
        _controller = new BansController(_orchestrator.Object, _repository.Object, _settings.Object);
        _settings.Setup(x => x.GetAsync(_coordinator.Id, default)).ReturnsAsync(_coordinator);
    }

    [Fact]
    public async Task OfflineBans_AreScoped_AndOnlyReturnActiveEntries()
    {
        var now = DateTime.UtcNow;
        var active = new GlobalBan(Guid.NewGuid(), _coordinator.Id, BanKind.Account, 1, null, "Active reason", 2, now, null);
        var expired = active with { Id = Guid.NewGuid(), Reason = "Expired reason", ExpiresAt = now.AddHours(-1) };
        _repository.Setup(x => x.GetAsync(_coordinator.Id, default)).ReturnsAsync([active, expired]);
        var current = await _controller.Get(_coordinator.Id);
        Assert.Equal(active.Id, Assert.Single(current.Value!).Id);
        Assert.IsType<NotFoundResult>((await _controller.Get("other")).Result);
    }

    [Fact]
    public async Task OfflineChanges_AreRefusedBeforeAnyWrite()
    {
        var create = Assert.IsType<ObjectResult>(await _controller.Create(_coordinator.Id,
            new BansController.CreateBan(1, BanKind.Account, "Reason")));
        var revoke = Assert.IsType<ObjectResult>(await _controller.Revoke(_coordinator.Id, Guid.NewGuid()));
        Assert.Equal(409, create.StatusCode);
        Assert.Equal(409, revoke.StatusCode);
        _repository.Verify(x => x.AddAsync(It.IsAny<GlobalBan>(), default), Times.Never);
    }

    [Theory]
    [InlineData("  Spam  ", "Spam")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public async Task PanelCreate_UsesTheSameModerationService_AndRecordsReasonExpiryAndSystemActor(string? reason, string expected)
    {
        var entries = new List<GlobalBan>();
        _repository.Setup(x => x.GetAsync(_coordinator.Id, default)).ReturnsAsync(() => entries.ToList());
        _repository.Setup(x => x.AddAsync(It.IsAny<GlobalBan>(), default)).Callback((GlobalBan ban, CancellationToken _) => entries.Add(ban)).Returns(Task.CompletedTask);
        var user = UserModel.Create(_coordinator.Id, "alice", "192.0.2.1", DateTime.UtcNow);
        user.Id = 1;
        var users = new Mock<IUserManager>();
        users.Setup(x => x.GetUserAsync(1)).ReturnsAsync(user);
        var connections = new Mock<IConnectionManager>();
        connections.Setup(x => x.GetAllConnections()).Returns([]);
        using var moderation = new ModerationService(_coordinator.Id, _repository.Object, Mock.Of<IUserRepository>(),
            users.Object, connections.Object, Mock.Of<IHybridSessionManager>(), Mock.Of<ICoordinatorPermissions>(),
            TimeProvider.System, NullLogger.Instance);
        var running = new Mock<ICoordinatorInstance>();
        running.Setup(x => x.Moderation).Returns(moderation);
        _orchestrator.Setup(x => x.GetCoordinator(_coordinator.Id)).Returns(running.Object);
        var expiry = DateTime.UtcNow.AddDays(1);
        Assert.IsType<NoContentResult>(await _controller.Create(_coordinator.Id,
            new BansController.CreateBan(1, BanKind.Account, reason, expiry)));
        var saved = Assert.Single(entries);
        Assert.Equal(expected, saved.Reason);
        Assert.Equal(-1, saved.CreatedBy);
        Assert.Equal(expiry, saved.ExpiresAt);
        Assert.Equal(1, saved.UserId);
        var unsupported = Assert.IsType<ObjectResult>(await _controller.Create(_coordinator.Id,
            new BansController.CreateBan(1, BanKind.Hardware, "Reason")));
        Assert.Equal(400, unsupported.StatusCode);
        Assert.Single(entries);
    }
}
