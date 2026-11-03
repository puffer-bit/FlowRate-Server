using Microsoft.Extensions.Logging;
using Shared.Enums;
using Server.Core.Models.HybridSession;
using Server.Application.Orchestrator.SFUPool;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Authentication;

namespace Server.Application.Coordinator.Sessions.Hybrid;

public class HybridSession : IHybridSession
{
    public HybridSessionModel Model { get; }
    public IHybridSessionPeerManager PeerManager { get; }
    private readonly ISFUNodeSelector _nodeSelector;
    
    private readonly ILogger _logger;
    
    public HybridSession (
        HybridSessionModel model,
        IHybridSessionPeerManager hybridSessionPeerManager,
        ISFUNodeSelector nodeSelector,
        ILogger logger)
    {
        Model = model;
        
        _logger = logger;
        _nodeSelector = nodeSelector;
        
        PeerManager = hybridSessionPeerManager;
    }

    public async Task CloseSession()
    {
        await PeerManager.KickAllPeersAsync();
    }

    public async Task AssignSfuNodeAsync()
    {
        var current = PeerManager.CurrentGateway;
        if (current is { IsConnected: true } && _nodeSelector.Contains(current))
            return;

        var nodeGateway = _nodeSelector.GetBestSFUNode(Model.Id);
        // With no reachable alternative, follow the existing connection until it recovers.
        if (current != null && _nodeSelector.Contains(current) && nodeGateway is not { IsConnected: true })
            return;

        if (nodeGateway != null && current != null && !ReferenceEquals(current, nodeGateway))
            _logger.LogInformation("Hybrid session {SessionId} moves to SFU node {NodeId}: its node is unavailable or left the pool.",
                Model.Id, nodeGateway.NodeId);
        else if (nodeGateway != null && Model.State == HybridSessionNodeState.WaitingForSFUNode)
            _logger.LogInformation("Hybrid session {SessionId} got SFU node {NodeId}.", Model.Id, nodeGateway.NodeId);

        // Without a node the session waits for one, so nobody keeps using a disabled or unassigned node
        await PeerManager.ChangeGatewayAsync(nodeGateway);
    }
    
    public void Dispose() => PeerManager.Dispose();
}
