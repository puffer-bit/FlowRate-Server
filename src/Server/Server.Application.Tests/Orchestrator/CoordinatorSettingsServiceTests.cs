using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Net;
using Server.Application.Abstractions.Gateway;
using Server.Application.Abstractions.Persistence;
using Server.Application.Abstractions.Security;
using Server.Application.Orchestrator.Settings;
using Server.Core.Models.Coordinator;
using Server.Core.Models.Coordinator.Settings;
using Server.Core.Models.Orchestrator;

namespace Server.Application.Tests.Orchestrator;

public class CoordinatorSettingsServiceTests
{
    private readonly FakeCoordinatorRepository _repository = new();
    private readonly CoordinatorSettingsStore _store;
    private readonly CoordinatorSettingsService _service;
    private readonly Mock<IListenEndpointProbe> _probe = new();
    private readonly CoordinatorInstanceModel _model =
        CoordinatorInstanceModel.Create("coordinator", "127.0.0.1", 5000, DateTime.UtcNow, passwordHash: "old-hash");

    public CoordinatorSettingsServiceTests()
    {
        _repository.Seed(_model);
        _store = new CoordinatorSettingsStore(_repository);

        var nodes = new Mock<ISfuNodeStore>();
        nodes.Setup(n => n.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new SfuNodeSettingsModel { Id = 1, Name = "b", Host = "10.0.0.1", Port = 50051, Username = "u", Key = "k" },
            new SfuNodeSettingsModel { Id = 2, Name = "a", Host = "10.0.0.2", Port = 50051, Username = "u", Key = "k" }
        ]);

        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.HashPassword(It.IsAny<string?>())).Returns((string? raw) => $"hash:{raw}");

        _probe.Setup(p => p.IsLocalAddress(It.IsAny<IPAddress>())).Returns(true);
        _probe.Setup(p => p.IsPortFree(It.IsAny<IPAddress>(), It.IsAny<int>())).Returns(true);

        _service = new CoordinatorSettingsService(_store, nodes.Object, hasher.Object, _probe.Object,
            NullLogger<CoordinatorSettingsService>.Instance);
    }

    [Fact]
    public async Task Update_SavesTheChangeUnderTheNextRevision()
    {
        var result = await _service.UpdateAsync(_model.Id, Change(), expectedRevision: 0);

        Assert.Equal(CoordinatorSettingsUpdateStatus.Updated, result.Status);
        var saved = (await _store.GetAsync(_model.Id))!;
        Assert.Equal(1, saved.Revision);
        Assert.Equal(1, result.Settings!.Revision);
        Assert.Equal("Renamed", saved.Name);
        Assert.Equal(6000, saved.Port);
        Assert.Equal(30, saved.Heartbeat.TimeoutSeconds);
        Assert.Equal("Hello", saved.Motd.Text);
        Assert.Equal([2], saved.SfuNodeIds);
        Assert.Equal(new CoordinatorUploadSettings(5, 2, 3), saved.Uploads);
        Assert.Equal(new RequestRateSetting(50, 10), saved.RateLimits.ChatWrite);
        Assert.Equal(100, saved.RateLimits.MaxConnectionsPerIp);
        Assert.Equal("old-hash", saved.PasswordHash);
    }

    [Fact]
    public async Task Update_TellsWhoeverListens()
    {
        var changed = new List<string>();
        _service.SettingsChanged += changed.Add;

        await _service.UpdateAsync(_model.Id, Change(), expectedRevision: 0);

        Assert.Equal([_model.Id], changed);
    }

    [Fact]
    public async Task Update_FromAnOlderRevision_IsAConflictAndNotSaved()
    {
        await _service.UpdateAsync(_model.Id, Change(), expectedRevision: 0);
        var changed = 0;
        _service.SettingsChanged += _ => changed++;

        var result = await _service.UpdateAsync(_model.Id, Change() with { Name = "Second" }, expectedRevision: 0);

        Assert.Equal(CoordinatorSettingsUpdateStatus.Conflict, result.Status);
        Assert.Equal("Renamed", result.Settings!.Name);
        Assert.Equal("Renamed", (await _store.GetAsync(_model.Id))!.Name);
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task Update_TwoAtOnceFromOneRevision_OnlyOneWins()
    {
        var results = await Task.WhenAll(
            _service.UpdateAsync(_model.Id, Change() with { Name = "First" }, expectedRevision: 0),
            _service.UpdateAsync(_model.Id, Change() with { Name = "Second" }, expectedRevision: 0));

        Assert.Single(results, r => r.Status == CoordinatorSettingsUpdateStatus.Updated);
        Assert.Single(results, r => r.Status == CoordinatorSettingsUpdateStatus.Conflict);
    }

    [Fact]
    public async Task Update_WithInvalidValues_ReportsThemByFieldAndSavesNothing()
    {
        var result = await _service.UpdateAsync(_model.Id,
            Change() with { SfuNodeIds = [2, 9], HeartbeatTimeoutSeconds = 5 }, expectedRevision: 0);

        Assert.Equal(CoordinatorSettingsUpdateStatus.Invalid, result.Status);
        Assert.Contains(CoordinatorSettingsFields.SfuNodeIds, result.Errors.Keys);
        Assert.Contains(CoordinatorSettingsFields.HeartbeatTimeout, result.Errors.Keys);
        Assert.Equal(0, (await _store.GetAsync(_model.Id))!.Revision);
    }

    [Fact]
    public async Task Update_ToAnAddressThisMachineHasNot_IsRefusedForTheIp()
    {
        _probe.Setup(p => p.IsLocalAddress(IPAddress.Parse("192.0.2.10"))).Returns(false);

        var result = await _service.UpdateAsync(_model.Id, Change() with { Ip = "192.0.2.10", Port = 5000 },
            expectedRevision: 0);

        Assert.Equal(CoordinatorSettingsUpdateStatus.Invalid, result.Status);
        Assert.Equal([CoordinatorSettingsFields.Ip], result.Errors.Keys);
    }

    [Fact]
    public async Task Update_ToATakenPort_IsRefusedForThePort()
    {
        _probe.Setup(p => p.IsPortFree(It.IsAny<IPAddress>(), 6000)).Returns(false);

        var result = await _service.UpdateAsync(_model.Id, Change(), expectedRevision: 0);

        Assert.Equal([CoordinatorSettingsFields.Port], result.Errors.Keys);
    }

    [Fact]
    public async Task Update_KeepingThePort_DoesNotTryIt()
    {
        // The coordinator holds its own port, so trying it would always fail
        await _service.UpdateAsync(_model.Id, Change() with { Ip = "127.0.0.2", Port = 5000 }, expectedRevision: 0);

        _probe.Verify(p => p.IsPortFree(It.IsAny<IPAddress>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Update_OfAnUnknownCoordinator_IsNotFound()
    {
        var result = await _service.UpdateAsync("missing", Change(), expectedRevision: 0);

        Assert.Equal(CoordinatorSettingsUpdateStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Update_WithANewPassword_HashesIt()
    {
        await _service.UpdateAsync(_model.Id, Change() with { NewPassword = "secret" }, expectedRevision: 0);

        Assert.Equal("hash:secret", (await _store.GetAsync(_model.Id))!.PasswordHash);
    }

    [Fact]
    public async Task Update_RemovingThePassword_ClearsIt()
    {
        await _service.UpdateAsync(_model.Id, Change() with { RemovePassword = true }, expectedRevision: 0);

        Assert.Null((await _store.GetAsync(_model.Id))!.PasswordHash);
    }

    [Fact]
    public async Task SfuNodes_AreListedByName()
    {
        var nodes = await _service.GetSfuNodesAsync();

        Assert.Equal(["a", "b"], nodes.Select(node => node.Name));
    }

    private static CoordinatorSettingsChange Change() => new()
    {
        Name = "Renamed",
        Ip = "127.0.0.1",
        Port = 6000,
        MaxOnlineUsers = 10,
        MaxUsers = 100,
        IsMotdEnabled = true,
        MotdText = "Hello",
        HeartbeatIntervalSeconds = 5,
        HeartbeatTimeoutSeconds = 30,
        MaxFileSizeBytes = 1024,
        StorageQuotaBytes = 0,
        MaxUnattachedUploadsPerUser = 5,
        UnattachedUploadLifetimeHours = 2,
        MediaTokenLifetimeMinutes = 3,
        RateLimits = RateLimitsChange.From(CoordinatorRateLimitSettings.Default) with
        {
            ChatWrite = new RequestRateChange(50, 10),
            MaxConnectionsPerIp = 100
        },
        SfuNodeIds = [2]
    };

    // Hands out copies, as the database does
    private sealed class FakeCoordinatorRepository : ICoordinatorInstanceRepository
    {
        private readonly Dictionary<string, CoordinatorInstanceModel> _models = new();

        public void Seed(CoordinatorInstanceModel model) => _models[model.Id] = Copy(model);

        public Task<CoordinatorInstanceModel?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_models.TryGetValue(id, out var model) ? Copy(model) : null);

        public Task<IReadOnlyCollection<CoordinatorInstanceModel>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<CoordinatorInstanceModel>>(_models.Values.Select(Copy).ToList());

        public Task AddAsync(CoordinatorInstanceModel instance, CancellationToken cancellationToken = default)
        {
            Seed(instance);
            return Task.CompletedTask;
        }

        public async Task UpdateAsync(CoordinatorInstanceModel instance, CancellationToken cancellationToken = default)
        {
            // Lets a second change in flight reach the revision check before this one is stored
            await Task.Yield();
            Seed(instance);
        }

        public Task RemoveAsync(string id, CancellationToken cancellationToken = default)
        {
            _models.Remove(id);
            return Task.CompletedTask;
        }

        private static CoordinatorInstanceModel Copy(CoordinatorInstanceModel m) =>
            new(m.Id, m.Name, m.IsDefault, m.Ip, m.Port, m.CreateTime, m.FirstLaunchTime, m.CurrentLaunchTime,
                m.MaxOnlineUsers, m.MaxUsers, m.Motd, m.AccessList, m.Heartbeat, m.Media, m.Uploads, m.RateLimits,
                m.SfuNodeIds, m.PasswordHash, m.Revision);
    }
}
