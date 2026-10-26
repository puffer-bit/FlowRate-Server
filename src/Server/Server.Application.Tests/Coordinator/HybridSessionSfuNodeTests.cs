using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Abstractions.Sfu;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Orchestrator.SFUPool;
using Server.Application.Tests.Fakes;
using Server.Core.Models.HybridSession;
using Shared.Enums;
using Shared.Enums.WebRTC;

namespace Server.Application.Tests.Coordinator;

public class HybridSessionSfuNodeTests
{
    private readonly Dictionary<int, ISFUNode> _registryNodes = new();
    private readonly Mock<ISfuNodeRegistry> _registry = new();
    private readonly RecordingEventBus _events = new();
    private readonly FakeSfuGateway _first = new() { NodeId = 1 };
    private readonly FakeSfuGateway _second = new() { NodeId = 2 };

    public HybridSessionSfuNodeTests()
    {
        _registry.Setup(r => r.GetNode(It.IsAny<int>())).Returns((int id) => _registryNodes.GetValueOrDefault(id));
        AddToRegistry(_first);
        AddToRegistry(_second);
    }

    private void AddToRegistry(FakeSfuGateway gateway)
    {
        var node = new Mock<ISFUNode>();
        node.Setup(n => n.Gateway).Returns(gateway);
        _registryNodes[gateway.NodeId] = node.Object;
    }

    private async Task<(HybridSession session, CoordinatorSfuPool pool)> CreateSession(params int[] nodeIds)
    {
        var pool = new CoordinatorSfuPool(_registry.Object, nodeIds);
        var model = HybridSessionModel.Create("coordinator", "room", 10, 1, WebRTCEngine.Mediasoup);
        model.Id = 7;

        var session = new HybridSession(model, new HybridSessionPeerManager(NullLogger.Instance, _events, model), pool,
            NullLogger.Instance);
        await session.AssignSfuNodeAsync();
        return (session, pool);
    }

    [Fact]
    public async Task AssignSfuNode_KeepsTheNodeWhileItIsInThePool()
    {
        var (session, _) = await CreateSession(1, 2);
        var before = session.PeerManager.CurrentGateway;

        await session.AssignSfuNodeAsync();

        Assert.Same(before, session.PeerManager.CurrentGateway);
    }

    [Fact]
    public async Task AssignSfuNode_AfterTheNodeWasUnassigned_MovesToAnotherNode()
    {
        var (session, pool) = await CreateSession(1, 2);
        Assert.Same(_first, session.PeerManager.CurrentGateway);

        pool.SetNodes([2]);
        await session.AssignSfuNodeAsync();

        Assert.Same(_second, session.PeerManager.CurrentGateway);
    }

    [Fact]
    public async Task AssignSfuNode_AfterTheNodeWasDisabled_MovesToAnotherNode()
    {
        var (session, _) = await CreateSession(1, 2);

        // A disabled node is dropped from the registry
        _registryNodes.Remove(1);
        await session.AssignSfuNodeAsync();

        Assert.Same(_second, session.PeerManager.CurrentGateway);
    }

    [Fact]
    public async Task AssignSfuNode_WithoutNodesLeft_WaitsForANode()
    {
        var (session, _) = await CreateSession(1);

        _registryNodes.Remove(1);
        await session.AssignSfuNodeAsync();

        Assert.Null(session.PeerManager.CurrentGateway);
        Assert.False(session.PeerManager.IsReady);
        Assert.Equal(HybridSessionNodeState.WaitingForSfuNode, session.Model.State);
    }

    [Fact]
    public async Task AssignSfuNode_OfADetachedSession_TakesANodeThatCameBack()
    {
        var (session, pool) = await CreateSession(1);
        pool.SetNodes([]);
        await session.AssignSfuNodeAsync();

        pool.SetNodes([2]);
        await session.AssignSfuNodeAsync();

        Assert.Same(_second, session.PeerManager.CurrentGateway);
        Assert.Equal(HybridSessionNodeState.Active, session.Model.State);
    }

    [Fact]
    public void Pool_RaisesNodesChangedOnlyWhenTheNodesDiffer()
    {
        var pool = new CoordinatorSfuPool(_registry.Object, [1, 2]);
        var raised = 0;
        pool.NodesChanged += () =>
        {
            raised++;
            return Task.CompletedTask;
        };

        pool.SetNodes([2, 1]);
        pool.SetNodes([2]);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Pool_SubscribesToTheRegistryAndReleasesIt()
    {
        var pool = new CoordinatorSfuPool(_registry.Object, [1]);
        Func<Task> handler = () => Task.CompletedTask;

        pool.NodesChanged += handler;
        pool.NodesChanged -= handler;

        _registry.VerifyAdd(r => r.NodesChanged += handler, Times.Once);
        _registry.VerifyRemove(r => r.NodesChanged -= handler, Times.Once);
    }

    [Fact]
    public void Pool_ContainsOnlyGatewaysOfItsNodes()
    {
        var pool = new CoordinatorSfuPool(_registry.Object, [1]);

        Assert.True(pool.Contains(_first));
        Assert.False(pool.Contains(_second));
        Assert.False(pool.Contains(new FakeSfuGateway { NodeId = 1 }));
    }
}
