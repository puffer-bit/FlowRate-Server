using Server.Application.Orchestrator.SFUPool;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Server.Application.Orchestrator.Settings;
using Server.Asp.Web.Api;
using Server.Asp.Web.Api.Contracts;
using Server.Core.Models.Coordinator;

namespace Server.Asp.Tests.Web.Api;

public class CoordinatorSettingsControllerTests
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    private readonly Mock<ICoordinatorSettingsService> _service = new();
    private readonly CoordinatorInstanceModel _model =
        CoordinatorInstanceModel.Create("coordinator", "127.0.0.1", 5000, DateTime.UtcNow, passwordHash: "old-hash");
    private readonly CoordinatorSettingsController _controller;

    public CoordinatorSettingsControllerTests()
    {
        _service.Setup(s => s.GetAsync(_model.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_model);
        _controller = new CoordinatorSettingsController(_service.Object, new Mock<ISfuNodeRegistry>().Object);
    }

    [Fact]
    public async Task Get_Unknown_IsNotFound()
    {
        var result = await _controller.GetSettings("missing", default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Get_TellsWhetherThereIsAPasswordButNotTheHash()
    {
        var settings = (await _controller.GetSettings(_model.Id, default)).Value!;

        Assert.True(settings.IsPasswordProtected);
        Assert.Equal("coordinator", settings.Name);
    }

    [Fact]
    public async Task Update_HandsTheChangeAndTheRevisionToTheService()
    {
        Answer(CoordinatorSettingsUpdateStatus.Updated);

        var result = await _controller.UpdateSettings(_model.Id, Update(expectedRevision: 4), default);

        Assert.Equal("coordinator", result.Value!.Name);
        _service.Verify(s => s.UpdateAsync(_model.Id, It.Is<CoordinatorSettingsChange>(c =>
            c.Name == "Renamed" && c.Port == 6000 && c.HeartbeatTimeoutSeconds == 30 && c.MotdText == "Hello"
            && c.SfuNodeIds.SequenceEqual(new[] { 2 }) && c.NewPassword == "secret"
            && c.MaxUnattachedUploadsPerUser == 5 && c.MediaTokenLifetimeMinutes == 3
            && c.RateLimits.ChatWrite == new RequestRateChange(50, 10) && c.RateLimits.MaxConnectionsPerIp == 100), 4,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_OfAChangedRevision_IsAConflict()
    {
        Answer(CoordinatorSettingsUpdateStatus.Conflict);

        var result = await _controller.UpdateSettings(_model.Id, Update(), default);

        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(409, problem.StatusCode);
    }

    [Fact]
    public async Task Update_WithRefusedValues_IsAValidationProblemByField()
    {
        Answer(CoordinatorSettingsUpdateStatus.Invalid,
            new Dictionary<string, string[]> { [CoordinatorSettingsFields.SfuNodeIds] = ["There is no SFU node 9."] });

        var result = await _controller.UpdateSettings(_model.Id, Update(), default);

        var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<ObjectResult>(result.Result).Value);
        Assert.Equal(["There is no SFU node 9."], problem.Errors[CoordinatorSettingsFields.SfuNodeIds]);
    }

    [Fact]
    public async Task Update_OfAnUnknownCoordinator_IsNotFound()
    {
        _service.Setup(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<CoordinatorSettingsChange>(),
                It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CoordinatorSettingsUpdateResult.NotFound);

        var result = await _controller.UpdateSettings("missing", Update(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    private void Answer(CoordinatorSettingsUpdateStatus status, IReadOnlyDictionary<string, string[]>? errors = null) =>
        _service.Setup(s => s.UpdateAsync(_model.Id, It.IsAny<CoordinatorSettingsChange>(), It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CoordinatorSettingsUpdateResult(status, _model, errors ?? NoErrors));

    private static UpdateCoordinatorSettingsDto Update(long expectedRevision = 0) => new()
    {
        ExpectedRevision = expectedRevision,
        Name = "Renamed",
        Ip = "127.0.0.1",
        Port = 6000,
        NewPassword = "secret",
        MaxOnlineUsers = 10,
        MaxUsers = 100,
        Motd = new MotdSettingsDto { IsEnabled = true, Text = "Hello" },
        Heartbeat = new HeartbeatSettingsDto { IntervalSeconds = 5, TimeoutSeconds = 30 },
        Media = new MediaSettingsDto { MaxFileSizeBytes = 1024, StorageQuotaBytes = 0 },
        Uploads = new UploadSettingsDto { MaxUnattachedPerUser = 5, UnattachedLifetimeHours = 2, TokenLifetimeMinutes = 3 },
        RateLimits = new RateLimitSettingsDto
        {
            ChatWrite = new RequestRateDto { Count = 50, PeriodSeconds = 10 }, MaxConnectionsPerIp = 100
        },
        SfuNodeIds = [2]
    };
}
