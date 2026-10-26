using Server.Infrastructure.Coordinator.Services.Mediasoup;
using Server.Infrastructure.Orchestrator.SFUPool;
using Server.Application.Orchestrator.SFUPool.Models;
using Server.Application.Abstractions.Sfu;

namespace Server.Asp.Factories;

public class SFUNodeFactory : ISFUNodeFactory
{
    private readonly IServiceProvider _provider;

    public SFUNodeFactory(IServiceProvider provider)
    {
        _provider = provider;
    }
    
    public ISFUNode CreateSFUNode(SFUNodeModel model)
    {
        var logger = _provider.GetRequiredService<ILoggerFactory>().CreateLogger($"SFUNode {model.Id}");
        
        var newSfuGateway = ActivatorUtilities.CreateInstance<MediasoupGateway>(_provider, logger, model);
        var newSfuNode = ActivatorUtilities.CreateInstance<SFUNode>(_provider, logger, model, newSfuGateway);
        
        return newSfuNode;
    }
}