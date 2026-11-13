using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Abstractions.Sfu;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Orchestrator.Settings;
using Server.Application.Orchestrator.SFUPool;
using Server.Application.Orchestrator.SFUPool.Models;
using Server.Application.Tests.Fakes;
using Server.Core.Models.HybridSession;
using Server.Core.Models.Orchestrator;
using Shared.Enums;
using Shared.Enums.WebRTC;

namespace Server.Application.Tests.Coordinator;

public class SfuNodeRecoveryTests
{
    private readonly FakeSfuGateway _first = new() { NodeId = 1 };
    private readonly FakeSfuGateway _second = new() { NodeId = 2 };
    private readonly List<SfuNodeSettingsModel> _settings = [Settings(1), Settings(2)];
    private readonly Mock<ISfuNodeStore> _store = new();

    private static SfuNodeSettingsModel Settings(int id) => new()
    {
        Id = id, Name = $"SFU {id}", Host = "localhost", Port = 50050 + id,
        Username = "test", Key = "test"
    };

    private async Task<SfuNodeRegistry> CreateRegistry()
    {
        _store.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _settings.ToArray());
        var factory = new Mock<ISFUNodeFactory>();
        factory.Setup(f => f.CreateSFUNode(It.IsAny<SFUNodeModel>())).Returns((SFUNodeModel model) =>
        {
            var node = new Mock<ISFUNode>();
            node.SetupGet(n => n.Model).Returns(model);
            node.SetupGet(n => n.Gateway).Returns(model.Id == 1 ? _first : _second);
            return node.Object;
        });
        var registry = new SfuNodeRegistry(NullLoggerFactory.Instance, factory.Object, _store.Object);
        await registry.LoadAsync();
        return registry;
    }

    [Fact]
    public async Task ConnectionLoss_MovesSessionThroughRegistryAndPool_AndRecoveryDoesNotMoveItBack()
    {
        var registry = await CreateRegistry();
        var pool = new CoordinatorSfuPool(registry, [1, 2]);
        var model = HybridSessionModel.Create("coordinator", "room", 10, 1, WebRTCEngine.Mediasoup);
        model.Id = 1;
        var events = new RecordingEventBus();
        using var session = new HybridSession(model,
            new HybridSessionPeerManager(NullLogger.Instance, events, model), pool, NullLogger.Instance);
        pool.NodesChanged += session.AssignSfuNodeAsync;
        await session.AssignSfuNodeAsync();
        await session.PeerManager.AddPeerAsync(1, "epoch");

        await _first.RaiseConnectionLost();

        Assert.Same(_second, session.PeerManager.CurrentGateway);
        Assert.True(session.PeerManager.HasPeer(1));
        Assert.Null((await session.PeerManager.AddPeerAsync(1, "new-epoch")).result);

        await _first.RaiseConnected();

        Assert.Same(_second, session.PeerManager.CurrentGateway);
        Assert.Equal(HybridSessionNodeState.Active, model.State);
        Assert.Equal(1, _second.CallCount("LoadSession"));
        Assert.Single(events.Of<Server.Application.Coordinator.Events.HybridSessionSFUNodeChangedEvent>());
        pool.NodesChanged -= session.AssignSfuNodeAsync;
    }

    [Fact]
    public async Task ConnectionRecovery_NotifiesAllCoordinatorsEvenIfOneHandlerFails()
    {
        var registry = await CreateRegistry();
        var notified = 0;
        registry.NodesChanged += () => throw new InvalidOperationException("one coordinator failed");
        registry.NodesChanged += () => { notified++; return Task.CompletedTask; };

        await _first.RaiseConnectionLost();
        await _first.RaiseConnected();

        Assert.Equal(2, notified);
    }

    [Fact]
    public async Task RemovedNode_DoesNotNotifyThePoolThroughOldConnectionEvents()
    {
        var registry = await CreateRegistry();
        _settings.RemoveAt(0);
        await _store.RaiseAsync(s => s.Changed += null);
        var notified = 0;
        registry.NodesChanged += () => { notified++; return Task.CompletedTask; };

        await _first.RaiseConnectionLost();
        await _first.RaiseConnected();

        Assert.Null(registry.GetNode(1));
        Assert.Equal(0, notified);
    }
}
