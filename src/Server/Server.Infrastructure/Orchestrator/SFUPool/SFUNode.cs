using Microsoft.Extensions.Logging;
using Server.Application.Orchestrator.SFUPool.Models;
using Server.Application.Abstractions.Sfu;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Users;
using Server.Application.Orchestrator.SFUPool;
using Server.Application.Orchestrator.Settings;

namespace Server.Infrastructure.Orchestrator.SFUPool;

public class SFUNode : ISFUNode
{
    public event Action? ConnectionWithNodeLost;
    
    public SFUNodeModel Model { get; private set; }
    public ISFUGateway Gateway { get; private set; }
    
    private readonly ILogger _logger;
    
    public SFUNode(ILogger logger, 
        SFUNodeModel model,
        ISFUGateway gateway)
    {
        _logger = logger;
        Gateway = gateway;
        Model = model;
    }

    public void ChangeModel(SFUNodeModel sfuNodeModel)
    {
        Model = sfuNodeModel;
    }
    
    public async Task Connect()
    {
        await Gateway.Connect();
    }

    public async Task Disconnect()
    {
        await Gateway.Disconnect();
    }
}