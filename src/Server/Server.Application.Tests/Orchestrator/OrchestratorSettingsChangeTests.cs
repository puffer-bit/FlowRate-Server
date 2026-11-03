using System.Net;
using Server.Application.Coordinator.Snapshots;
using Server.Core.Models.Orchestrator;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Core.Enums;
using Server.Application.Abstractions.Gateway;
using Server.Core.Models.Coordinator;
using Server.Application.Abstractions.Factories;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator;
using Server.Application.Options;
using Server.Application.Orchestrator;
using Server.Application.Orchestrator.Bootstrap;
using Server.Application.Orchestrator.SFUPool;
using Server.Application.Orchestrator.Settings;

namespace Server.Application.Tests.Orchestrator;

public class OrchestratorSettingsChangeTests
{
    private readonly FakeCoordinatorSettingsStore _store = new();
    private readonly Mock<ICoordinatorInstanceFactory> _factory = new();
    private readonly List<Mock<ICoordinatorInstance>> _created = [];
    // Ports a coordinator fails to start on, as when another program holds them
    private readonly HashSet<int> _takenPorts = [];
    private readonly OrchestratorInstance _orchestrator;
    private readonly Mock<IListenEndpointProbe> _probe = new();
    private readonly Mock<IOrchestratorInstanceContext> _context = new();

    public OrchestratorSettingsChangeTests()
    {
        _factory.Setup(f => f.CreateCoordinatorInstance(It.IsAny<string>(), It.IsAny<CoordinatorInstanceModel>()))
            .Returns((string version, CoordinatorInstanceModel model) =>
            {
                var coordinator = new Mock<ICoordinatorInstance>();
                coordinator.Setup(c => c.Context).Returns(new CoordinatorInstanceContext(model, version));
                coordinator.Setup(c => c.State).Returns(CoordinatorState.Online);
                coordinator.Setup(c => c.GetSnapshot()).Returns(() => new CoordinatorSnapshot(model.Id, model.Name,
                    model.Ip, model.Port, CoordinatorState.Online, model.CurrentLaunchTime, [], []));
                if (_takenPorts.Contains(model.Port))
                    coordinator.Setup(c => c.StartAsync()).ThrowsAsync(new InvalidOperationException("taken"));
                _created.Add(coordinator);
                return coordinator.Object;
            });

        var context = _context;
        context.Setup(c => c.ServerVersion).Returns("1.0");
        context.Setup(c => c.MaxCoordinators).Returns(4);
        _probe.Setup(p => p.IsLocalAddress(It.IsAny<IPAddress>())).Returns(true);
        _probe.Setup(p => p.IsPortFree(It.IsAny<IPAddress>(), It.IsAny<int>())).Returns(true);
        var nodes = new Mock<ISfuNodeStore>();
        nodes.Setup(n => n.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            [new SfuNodeSettingsModel { Id = 1, Name = "local", Host = "127.0.0.1", Port = 50051, Username = "", Key = "" }]);
        var seeder = new ConfigurationSeeder(Microsoft.Extensions.Options.Options.Create(new OrchestratorOptions()),
            new Mock<ISfuNodeStore>().Object, _store, NullLoggerFactory.Instance);

        _orchestrator = new OrchestratorInstance(context.Object, NullLoggerFactory.Instance, _factory.Object,
            new Mock<IOrchestratorInstanceRepository>().Object, _store, seeder, new Mock<ISfuNodeRegistry>().Object, _probe.Object, nodes.Object)
        {
            Context = context.Object,
            CoordinatorsPool = new()
        };
    }

    [Fact]
    public async Task Change_OfARunningCoordinator_IsAppliedWithoutARestart()
    {
        var model = await StartWithAsync();

        var changed = Copy(model);
        changed.Rename("Renamed");
        await _store.UpdateAsync(changed);

        var coordinator = Assert.Single(_created);
        coordinator.Verify(c => c.ApplySettings(It.Is<CoordinatorInstanceModel>(m => m.Name == "Renamed")), Times.Once);
        coordinator.Verify(c => c.CloseAsync(), Times.Never);
    }

    [Fact]
    public async Task Change_OfTheEndpoint_RestartsTheCoordinator()
    {
        var model = await StartWithAsync();

        var changed = Copy(model);
        changed.Listen("127.0.0.1", 6000);
        await _store.UpdateAsync(changed);

        await WaitUntil(() => _created.Count == 2 && _orchestrator.GetCoordinator(model.Id) != null);
        _created[0].Verify(c => c.CloseAsync(), Times.Once);
        _created[0].Verify(c => c.ApplySettings(It.IsAny<CoordinatorInstanceModel>()), Times.Never);
        _created[1].Verify(c => c.StartAsync(), Times.Once);
        Assert.Same(_created[1].Object, _orchestrator.GetCoordinator(model.Id));
    }

    [Fact]
    public async Task Change_ToAnEndpointThatCannotBeTaken_KeepsTheCoordinatorOnTheOldOne()
    {
        var model = await StartWithAsync();
        _takenPorts.Add(6000);

        var changed = Copy(model);
        changed.Listen("127.0.0.1", 6000);
        await _store.UpdateAsync(changed);

        await WaitUntil(() => _created.Count == 3 && _orchestrator.GetCoordinator(model.Id) != null);
        var restored = _orchestrator.GetCoordinator(model.Id)!;
        Assert.Same(_created[2].Object, restored);
        Assert.Equal(5000, restored.Context.Model.Port);
        _created[2].Verify(c => c.StartAsync(), Times.Once);
    }

    [Fact]
    public async Task Change_OfACoordinatorThatIsNotRunning_StartsIt()
    {
        await _orchestrator.StartAsync();

        await _store.AddAsync(Coordinator());

        var coordinator = Assert.Single(_created);
        coordinator.Verify(c => c.StartAsync(), Times.Once);
    }

    [Fact]
    public async Task Change_AfterStop_IsIgnored()
    {
        var model = await StartWithAsync();
        await _orchestrator.StopAsync();

        await _store.UpdateAsync(Copy(model));

        Assert.Single(_created).Verify(c => c.ApplySettings(It.IsAny<CoordinatorInstanceModel>()), Times.Never);
    }

    [Fact]
    public async Task Change_ThatFailsToApply_DoesNotFailTheSave()
    {
        var model = await StartWithAsync();
        _created[0].Setup(c => c.ApplySettings(It.IsAny<CoordinatorInstanceModel>()))
            .Throws<InvalidOperationException>();

        await _store.UpdateAsync(Copy(model));

        Assert.Equal(ServerState.Online, _orchestrator.State);
    }

    [Fact]
    public async Task Coordinator_StartedAndStopped_IsAnnouncedChanged()
    {
        var changed = new List<string>();
        _orchestrator.CoordinatorChanged += changed.Add;

        var model = await StartWithAsync();
        Assert.Equal([model.Id, model.Id], changed);

        await _orchestrator.StopAsync();
        Assert.Equal([model.Id, model.Id, model.Id], changed);
    }

    [Fact]
    public async Task Coordinator_ThatFailsToStart_IsAnnouncedChanged()
    {
        var changed = new List<string>();
        _orchestrator.CoordinatorChanged += changed.Add;
        _takenPorts.Add(5000);
        _store.Seed(Coordinator());
        _store.Seed(CoordinatorInstanceModel.Create("other", "127.0.0.1", 5001, DateTime.UtcNow));

        await _orchestrator.StartAsync();

        Assert.Equal(4, changed.Count);
        Assert.Single(_orchestrator.CoordinatorsPool);
    }

    [Fact]
    public async Task Create_InitializesSavesAndStartsAnIndependentCoordinator()
    {
        var first = await StartWithAsync();
        var result = await _orchestrator.CreateCoordinatorInstanceAsync(new(" Second ", " 127.0.0.1 ", 5001, [1]));

        Assert.Equal(CoordinatorCreationStatus.Created, result.Status);
        Assert.Equal(CoordinatorState.Online, result.State);
        var saved = (await _store.GetAsync(result.Settings!.Id))!;
        Assert.NotEqual(first.Id, saved.Id);
        Assert.Equal("Second", saved.Name);
        Assert.Equal((64, 256), (saved.MaxOnlineUsers, saved.MaxUsers));
        Assert.Equal((5, 40), (saved.Heartbeat.IntervalSeconds, saved.Heartbeat.TimeoutSeconds));
        Assert.Equal([1], saved.SfuNodeIds);
        Assert.Equal(2, _orchestrator.CoordinatorsPool.Count);
        _created[0].Verify(c => c.CloseAsync(), Times.Never);
        _created[1].Verify(c => c.StartAsync(), Times.Once);
    }

    [Theory]
    [InlineData("", "127.0.0.1", 5001, "Name")]
    [InlineData("new", "wrong", 5001, "Ip")]
    [InlineData("new", "127.0.0.1", 0, "Port")]
    public async Task Create_InvalidSettings_AreNotSaved(string name, string ip, int port, string field)
    {
        await _orchestrator.StartAsync();
        var result = await _orchestrator.CreateCoordinatorInstanceAsync(new(name, ip, port, []));
        Assert.Equal(CoordinatorCreationStatus.Invalid, result.Status);
        Assert.Contains(field, result.Errors!.Keys);
        Assert.Empty(await _store.GetAllAsync());
    }

    [Fact]
    public async Task Create_UnknownSfuNode_IsNotSaved()
    {
        await _orchestrator.StartAsync();
        var result = await _orchestrator.CreateCoordinatorInstanceAsync(new("new", "127.0.0.1", 5001, [99]));
        Assert.Equal(CoordinatorCreationStatus.Invalid, result.Status);
        Assert.Contains("SfuNodeIds", result.Errors!.Keys);
        Assert.Empty(_created);
    }

    [Fact]
    public async Task Create_NonLocalAddressAndTakenPort_AreNotSaved()
    {
        await _orchestrator.StartAsync();
        _probe.Setup(p => p.IsLocalAddress(It.IsAny<IPAddress>())).Returns(false);
        var addressResult = await _orchestrator.CreateCoordinatorInstanceAsync(new("new", "192.0.2.1", 5001, []));
        Assert.Contains("Ip", addressResult.Errors!.Keys);
        _probe.Setup(p => p.IsLocalAddress(It.IsAny<IPAddress>())).Returns(true);
        _probe.Setup(p => p.IsPortFree(It.IsAny<IPAddress>(), 5001)).Returns(false);
        var portResult = await _orchestrator.CreateCoordinatorInstanceAsync(new("new", "127.0.0.1", 5001, []));
        Assert.Contains("Port", portResult.Errors!.Keys);
        Assert.Empty(await _store.GetAllAsync());
    }

    [Fact]
    public async Task Create_ConcurrentRequests_CannotExceedLimit()
    {
        await _orchestrator.StartAsync();
        _context.Setup(c => c.MaxCoordinators).Returns(2);
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            _orchestrator.CreateCoordinatorInstanceAsync(new($"new{i}", "127.0.0.1", 5000 + i, []))));
        Assert.Equal(2, results.Count(r => r.Status == CoordinatorCreationStatus.Created));
        Assert.Equal(8, results.Count(r => r.Status == CoordinatorCreationStatus.LimitReached));
        Assert.Equal(2, (await _store.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Create_ConcurrentRequests_CannotReserveOverlappingEndpoints()
    {
        await _orchestrator.StartAsync();
        var results = await Task.WhenAll(
            _orchestrator.CreateCoordinatorInstanceAsync(new("one", "0.0.0.0", 5000, [])),
            _orchestrator.CreateCoordinatorInstanceAsync(new("two", "127.0.0.1", 5000, [])));
        Assert.Single(results, r => r.Status == CoordinatorCreationStatus.Created);
        Assert.Single(results, r => r.Status == CoordinatorCreationStatus.Invalid);
        Assert.Single(await _store.GetAllAsync());
    }

    [Fact]
    public async Task Create_FailedListener_StaysVisibleAndCanBeFixed()
    {
        await _orchestrator.StartAsync();
        _takenPorts.Add(5000); // The port becomes unavailable after its preflight check.
        var result = await _orchestrator.CreateCoordinatorInstanceAsync(new("new", "127.0.0.1", 5000, [1]));
        Assert.Equal(CoordinatorCreationStatus.Created, result.Status);
        Assert.Equal(CoordinatorState.Failed, result.State);
        Assert.Equal(CoordinatorState.Failed, Assert.Single(_orchestrator.GetCoordinatorSnapshots()).State);
        Assert.NotNull(await _store.GetAsync(result.Settings!.Id));
        _created[0].Verify(c => c.Dispose(), Times.Once);

        var changed = Copy(result.Settings);
        changed.Listen("127.0.0.1", 5001);
        await _store.UpdateAsync(changed);
        Assert.Equal(CoordinatorState.Online, Assert.Single(_orchestrator.GetCoordinatorSnapshots()).State);
    }

    [Fact]
    public async Task Create_WhenServerIsStopped_DoesNotSave()
    {
        await _orchestrator.StartAsync();
        await _orchestrator.StopAsync();
        var result = await _orchestrator.CreateCoordinatorInstanceAsync(new("new", "127.0.0.1", 5000, []));
        Assert.Equal(CoordinatorCreationStatus.Unavailable, result.Status);
        Assert.Empty(await _store.GetAllAsync());
    }

    // A restart runs off the change, so its outcome is waited for
    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The restart did not finish in time.");
            await Task.Delay(10);
        }
    }

    private async Task<CoordinatorInstanceModel> StartWithAsync()
    {
        var model = Coordinator();
        _store.Seed(model);
        await _orchestrator.StartAsync();
        return model;
    }

    private static CoordinatorInstanceModel Coordinator() =>
        CoordinatorInstanceModel.Create("coordinator", "127.0.0.1", 5000, DateTime.UtcNow);

    // The store hands out copies, as the database does
    private static CoordinatorInstanceModel Copy(CoordinatorInstanceModel m) =>
        new(m.Id, m.Name, m.IsDefault, m.Ip, m.Port, m.CreateTime, m.FirstLaunchTime, m.CurrentLaunchTime,
            m.MaxOnlineUsers, m.MaxUsers, m.Motd, m.AccessList, m.Heartbeat, m.Media, m.Uploads, m.RateLimits,
            m.SfuNodeIds, m.PasswordHash);

    private sealed class FakeCoordinatorSettingsStore : ICoordinatorSettingsStore
    {
        private readonly Dictionary<string, CoordinatorInstanceModel> _models = new();

        public event Func<string, Task>? Changed;

        public void Seed(CoordinatorInstanceModel model) => _models[model.Id] = Copy(model);

        public Task<IReadOnlyCollection<CoordinatorInstanceModel>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyCollection<CoordinatorInstanceModel>>(_models.Values.Select(Copy).ToList());

        public Task<CoordinatorInstanceModel?> GetAsync(string coordinatorId, CancellationToken ct = default) =>
            Task.FromResult(_models.TryGetValue(coordinatorId, out var model) ? Copy(model) : null);

        public Task AddAsync(CoordinatorInstanceModel coordinator, CancellationToken ct = default) =>
            UpdateAsync(coordinator, ct);

        public async Task UpdateAsync(CoordinatorInstanceModel coordinator, CancellationToken ct = default)
        {
            Seed(coordinator);
            await Task.Yield();
            if (Changed != null)
                await Changed(coordinator.Id);
        }
    }
}
