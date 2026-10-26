using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Core.Enums;
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

    public OrchestratorSettingsChangeTests()
    {
        _factory.Setup(f => f.CreateCoordinatorInstance(It.IsAny<string>(), It.IsAny<CoordinatorInstanceModel>()))
            .Returns((string version, CoordinatorInstanceModel model) =>
            {
                var coordinator = new Mock<ICoordinatorInstance>();
                coordinator.Setup(c => c.Context).Returns(new CoordinatorInstanceContext(model, version));
                if (_takenPorts.Contains(model.Port))
                    coordinator.Setup(c => c.StartAsync()).ThrowsAsync(new InvalidOperationException("taken"));
                _created.Add(coordinator);
                return coordinator.Object;
            });

        var context = new Mock<IOrchestratorInstanceContext>();
        context.Setup(c => c.ServerVersion).Returns("1.0");
        var seeder = new ConfigurationSeeder(Microsoft.Extensions.Options.Options.Create(new OrchestratorOptions()),
            new Mock<ISfuNodeStore>().Object, _store, NullLoggerFactory.Instance);

        _orchestrator = new OrchestratorInstance(context.Object, NullLoggerFactory.Instance, _factory.Object,
            new Mock<IOrchestratorInstanceRepository>().Object, _store, seeder, new Mock<ISfuNodeRegistry>().Object)
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
            if (Changed != null)
                await Changed(coordinator.Id);
        }
    }
}
