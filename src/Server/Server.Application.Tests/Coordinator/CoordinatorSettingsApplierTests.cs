using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Models;
using Shared.Models.Notifications.CoordinatorInfo;
using Server.Core.Models.Coordinator;
using Server.Core.Models.Coordinator.Settings;
using Server.Application.Coordinator;
using Server.Application.Coordinator.Connections.RateLimiting;
using Server.Application.Coordinator.Heartbeat;
using Server.Application.Coordinator.Media;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Settings;
using Server.Application.Coordinator.Users;
using Server.Application.Orchestrator.SFUPool;

namespace Server.Application.Tests.Coordinator;

public class CoordinatorSettingsApplierTests
{
    private static readonly DateTime LaunchedAt = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly CoordinatorInstanceModel _current = Model("Old");
    private readonly CoordinatorInstanceContext _context;
    private readonly Mock<IHeartbeatManager> _heartbeat = new();
    private readonly Mock<IMediaService> _media = new();
    private readonly Mock<IMessageSender> _sender = new();
    private readonly CoordinatorSfuPool _sfuPool = new(new Mock<ISfuNodeRegistry>().Object, [1]);
    private readonly RequestRateLimits _rateLimits = new(CoordinatorRateLimitSettings.Default);
    private readonly CoordinatorSettingsApplier _applier;

    public CoordinatorSettingsApplierTests()
    {
        _context = new CoordinatorInstanceContext(_current, "1.0");
        _current.MarkLaunched(LaunchedAt);

        var users = new Mock<IUserManager>();
        users.Setup(u => u.ConnectedUsersCount).Returns(3);

        _applier = new CoordinatorSettingsApplier(NullLogger.Instance, _context, _heartbeat.Object, _sfuPool, _sender.Object, users.Object,
            _rateLimits, _media.Object);
    }

    [Fact]
    public void Apply_ReplacesTheModelAndKeepsTheLaunchTime()
    {
        var changed = Model("New");

        _applier.Apply(changed);

        Assert.Same(changed, _context.Model);
        Assert.Equal("New", ((ICoordinatorInstanceContext)_context).Name);
        Assert.Equal(LaunchedAt, _context.Model.CurrentLaunchTime);
    }

    [Fact]
    public void Apply_HandsTheSettingsToTheServicesThatCopiedThem()
    {
        var changed = Model("New");
        changed.SetHeartbeat(new CoordinatorHeartbeatSettings(7, 70));
        var media = new CoordinatorMediaSettings(1024, 0);
        changed.SetMedia(media);
        changed.AssignSfuNodes([2, 3]);
        var uploads = new CoordinatorUploadSettings(1, 2, 3);
        changed.SetUploads(uploads);
        changed.SetRateLimits(CoordinatorRateLimitSettings.Default with { MaxConnectionsPerIp = 77 });
        var rateLimitsVersion = _rateLimits.Version;

        _applier.Apply(changed);

        _heartbeat.Verify(h => h.ApplySettings(new HeartbeatSettings(TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(70))));
        _media.Verify(m => m.SetLimits(media));
        _media.Verify(m => m.SetUploadSettings(uploads));
        Assert.Equal(77, _rateLimits.MaxConnectionsPerIp);
        Assert.NotEqual(rateLimitsVersion, _rateLimits.Version);
        Assert.Equal([2, 3], _sfuPool.NodeIds);
    }

    [Fact]
    public void Apply_TellsTheUsersTheNewSettings()
    {
        var changed = Model("New");
        changed.SetMotd(new MotdSettings(true, "Hello"));
        changed.SetPassword("hash");

        var sent = new List<BaseMessage>();
        _sender.Setup(s => s.BroadcastMessage(It.IsAny<BaseMessage>())).Callback<BaseMessage>(sent.Add);

        _applier.Apply(changed);

        var dto = Assert.IsType<CoordinatorReconfiguredNotification>(Assert.Single(sent)).CoordinatorSessionDTO;
        Assert.Equal("New", dto.Name);
        Assert.Equal("Hello", dto.MotdText);
        Assert.True(dto.IsPasswordProtected);
        Assert.Equal(3, dto.ConnectedUsersCount);
    }

    [Fact]
    public void Context_RejectsTheSettingsOfAnotherCoordinator()
    {
        var other = CoordinatorInstanceModel.Create("Other", "127.0.0.1", 5000, DateTime.UtcNow);

        Assert.Throws<ArgumentException>(() => _context.ApplySettings(other));
    }

    private static CoordinatorInstanceModel Model(string name) =>
        new("coordinator", name, true, "127.0.0.1", 5000, LaunchedAt, LaunchedAt, DateTime.UtcNow, 10, 100,
            MotdSettings.Disabled, AccessListSettings.Open, new CoordinatorHeartbeatSettings(5, 40),
            CoordinatorMediaSettings.Default, CoordinatorUploadSettings.Default, CoordinatorRateLimitSettings.Default, [1]);
}
