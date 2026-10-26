using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Core.Models.HybridSession;
using Server.Application.Abstractions.Factories;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Orchestrator.SFUPool;


namespace Server.Asp.Factories;

public class HybridSessionFactory : IVideoSessionFactory
{
    private readonly IServiceProvider _provider;

    public HybridSessionFactory(IServiceProvider provider)
    {
        _provider = provider;
    }

    public IHybridSession CreateHybridSession(HybridSessionModel sessionModel, ICoordinatorEventBus coordinatorEventBus,
        ISFUNodeSelector sfuNodeSelector)
    {
        var logger = _provider.GetRequiredService<ILoggerFactory>().CreateLogger($"Hybrid session {sessionModel.Id}");
        
        return ActivatorUtilities.CreateInstance<HybridSession>(_provider, logger, sessionModel,
            CreateHybridPeerManager(logger, sessionModel, coordinatorEventBus), sfuNodeSelector);
    }

#pragma warning disable CA1859
    
    private IHybridSessionPeerManager CreateHybridPeerManager(
        ILogger logger, 
        HybridSessionModel model, 
        ICoordinatorEventBus coordinatorEventBus) =>
        ActivatorUtilities.CreateInstance<HybridSessionPeerManager>(_provider, model, coordinatorEventBus, logger);
    
#pragma warning restore CA1859
}