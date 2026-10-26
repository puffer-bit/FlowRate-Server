using Server.Application.Abstractions.Sfu;
using Server.Application.Orchestrator.Settings;

namespace Server.Application.Orchestrator.SFUPool;

/// <summary>
/// The orchestrator's global pool of SFU nodes. Keeps one connection per node and follows changes in
/// <see cref="ISfuNodeStore"/>. Coordinators take their nodes from here through their own pool.
/// </summary>
public interface ISfuNodeRegistry
{
    /// <summary>Raised after nodes were added, removed (also disabled) or recreated with new settings.</summary>
    event Func<Task>? NodesChanged;

    /// <summary>Raised when a node connected, lost its connection or stopped answering pings.</summary>
    event Action? NodeStatusChanged;

    /// <summary>Creates the nodes stored in the database without connecting to them.</summary>
    Task LoadAsync(CancellationToken ct = default);

    /// <summary>Connects to every loaded node.</summary>
    Task ConnectAllAsync();

    ISFUNode? GetNode(int nodeId);

    /// <summary>Connection status of a node in the pool; null for a node that is not in it (unknown or disabled).</summary>
    SfuNodeStatus? GetStatus(int nodeId);
}
