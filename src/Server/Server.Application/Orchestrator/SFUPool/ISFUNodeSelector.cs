using Server.Application.Abstractions.Sfu;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Users;
using Server.Application.Orchestrator.Settings;

namespace Server.Application.Orchestrator.SFUPool;

public interface ISFUNodeSelector
{
    /// <summary>Raised when nodes or their availability change. Sessions without a healthy node can move.</summary>
    event Func<Task>? NodesChanged;

    ISFUGateway? GetBestSFUNode(int sessionId);
    ISFUGateway? GetSFUNodeById(int sessionId, int nodeId);

    /// <summary>Whether the gateway belongs to a node that is still enabled and assigned to this pool.</summary>
    bool Contains(ISFUGateway gateway);
}
