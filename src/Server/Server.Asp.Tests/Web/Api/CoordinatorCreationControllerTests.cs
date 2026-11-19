using Microsoft.AspNetCore.Mvc;
using Moq;
using Server.Application.Coordinator.Snapshots;
using Server.Application.Orchestrator;
using Server.Application.Orchestrator.Settings;
using Server.Asp.Web.Api;
using Server.Asp.Web.Api.Contracts;
using Server.Core.Enums;
using Server.Core.Models.Coordinator;

namespace Server.Asp.Tests.Web.Api;

public class CoordinatorCreationControllerTests
{
    private readonly Mock<IOrchestratorInstance> _orchestrator = new();
    private readonly PanelController _controller;

    public CoordinatorCreationControllerTests()
    {
        var context = new Mock<IOrchestratorInstanceContext>();
        context.Setup(c => c.MaxCoordinators).Returns(4);
        _orchestrator.Setup(o => o.Context).Returns(context.Object);
        _controller = new(_orchestrator.Object);
    }

    [Theory]
    [InlineData(CoordinatorState.Online)]
    [InlineData(CoordinatorState.Failed)]
    public async Task Create_ReturnsTheSavedIdAndActualState(CoordinatorState state)
    {
        var model = CoordinatorInstanceModel.Create("new", "127.0.0.1", 5001, DateTime.UtcNow);
        Answer(new(CoordinatorCreationStatus.Created, model, state));
        var result = await _controller.CreateCoordinator(Request(), default);
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var body = Assert.IsType<CoordinatorCreatedDto>(created.Value);
        Assert.Equal((model.Id, state), (body.Id, body.State));
        Assert.Equal(model.Id, created.RouteValues!["id"]);
        _orchestrator.Verify(o => o.CreateCoordinatorInstanceAsync(It.Is<CreateCoordinator>(c =>
            c.Name == "new" && c.Port == 5001 && c.SfuNodeIds.SequenceEqual(new[] { 1 })
            && c.MaxOnlineUsers == 64 && c.MaxUsers == 256), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_InvalidSettings_ReturnsErrorsByField()
    {
        Answer(new(CoordinatorCreationStatus.Invalid, Errors: new Dictionary<string, string[]> { ["Port"] = ["Taken"] }));
        var result = await _controller.CreateCoordinator(Request(), default);
        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(["Taken"], Assert.IsType<ValidationProblemDetails>(problem.Value).Errors["Port"]);
    }

    [Theory]
    [InlineData(CoordinatorCreationStatus.LimitReached, 409)]
    [InlineData(CoordinatorCreationStatus.Unavailable, 503)]
    public async Task Create_Refused_ReturnsProblem(CoordinatorCreationStatus status, int code)
    {
        Answer(new(status));
        var result = await _controller.CreateCoordinator(Request(), default);
        Assert.Equal(code, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public void FailedCoordinator_CanBeOpenedToFixItsSettings()
    {
        _orchestrator.Setup(o => o.GetCoordinatorSnapshots()).Returns([
            new CoordinatorSnapshot("failed", "failed", "127.0.0.1", 5001, CoordinatorState.Failed, DateTime.UtcNow, [], [])
        ]);
        var result = _controller.GetCoordinator("failed");
        Assert.Equal(CoordinatorState.Failed, result.Value!.Summary.State);
    }

    private void Answer(CoordinatorCreationResult result) =>
        _orchestrator.Setup(o => o.CreateCoordinatorInstanceAsync(It.IsAny<CreateCoordinator>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(result);

    private static CreateCoordinatorDto Request() => new() { Name = "new", Ip = "127.0.0.1", Port = 5001, SfuNodeIds = [1] };
}
