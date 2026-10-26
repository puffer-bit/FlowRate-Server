using Server.Application.Orchestrator.SFUPool.Models;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Users;
using Server.Application.Orchestrator.SFUPool;
using Server.Application.Orchestrator.Settings;


namespace Server.Application.Abstractions.Sfu;

public interface ISFUNode
{
    event Action ConnectionWithNodeLost;
    
    SFUNodeModel Model { get; }
    ISFUGateway Gateway { get; }

    Task Connect();
    Task Disconnect();
    
    void ChangeModel(SFUNodeModel sfuNodeModel);
}