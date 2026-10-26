using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Core.Models.HybridSession;
using Server.Application.Coordinator.Events;
using Server.Application.Orchestrator.SFUPool;
using Server.Application.Coordinator.Connections;




namespace Server.Application.Abstractions.Factories;

public interface IVideoSessionFactory
{
    IHybridSession CreateHybridSession(HybridSessionModel sessionModel,
        ICoordinatorEventBus coordinatorEventBus,
        ISFUNodeSelector sfuNodeSelector);
}