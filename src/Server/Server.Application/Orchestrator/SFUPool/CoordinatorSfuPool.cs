using Server.Application.Abstractions.Sfu;


namespace Server.Application.Orchestrator.SFUPool;

/// <summary>
/// The part of the global SFU pool a single coordinator is allowed to use. Sessions of the coordinator pick nodes here.
/// </summary>
public class CoordinatorSfuPool : ISFUNodeSelector
{
    private readonly ISfuNodeRegistry _registry;
    private volatile int[] _nodeIds;
    private Func<Task>? _nodesChanged;

    // Handlers are also attached to the registry, so unsubscribing releases the coordinator from the global pool
    public event Func<Task>? NodesChanged
    {
        add
        {
            _nodesChanged += value;
            _registry.NodesChanged += value;
        }
        remove
        {
            _nodesChanged -= value;
            _registry.NodesChanged -= value;
        }
    }

    public CoordinatorSfuPool(ISfuNodeRegistry registry, IReadOnlyCollection<int> nodeIds)
    {
        _registry = registry;
        _nodeIds = nodeIds.Distinct().Order().ToArray();
    }

    public IReadOnlyCollection<int> NodeIds => _nodeIds.ToArray();

    public void SetNodes(IEnumerable<int> nodeIds)
    {
        var newNodeIds = nodeIds.Distinct().Order().ToArray();
        var isChanged = !newNodeIds.SequenceEqual(_nodeIds);
        _nodeIds = newNodeIds;

        // Handlers log their own failures, so nothing is lost by not awaiting here
        if (isChanged)
            _ = _nodesChanged?.Invoke();
    }

    public ISFUGateway? GetBestSFUNode(int sessionId)
    {
        var nodes = _nodeIds
            .Select(_registry.GetNode)
            .OfType<ISFUNode>()
            .ToList();

        // Spread sessions over reachable nodes independently of the configured order.
        // An assigned, healthy session keeps its node when the pool grows.
        var connected = nodes.Where(node => node.Gateway.IsConnected).ToList();
        var candidates = connected.Count > 0 ? connected : nodes;
        return candidates.Count == 0 ? null : candidates[(int)((uint)(sessionId - 1) % (uint)candidates.Count)].Gateway;
    }

    public ISFUGateway? GetSFUNodeById(int sessionId, int nodeId) =>
        _nodeIds.Contains(nodeId) ? _registry.GetNode(nodeId)?.Gateway : null;

    public bool Contains(ISFUGateway gateway) =>
        _nodeIds.Any(nodeId => ReferenceEquals(_registry.GetNode(nodeId)?.Gateway, gateway));
}
