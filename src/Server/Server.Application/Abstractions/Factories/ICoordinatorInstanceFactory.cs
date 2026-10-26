using Server.Core.Models.Coordinator;
using Server.Application.Coordinator;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Sessions.Hybrid;

namespace Server.Application.Abstractions.Factories;

public interface ICoordinatorInstanceFactory
{
    public ICoordinatorInstance CreateCoordinatorInstance(string serverVersion,
        CoordinatorInstanceModel coordinatorInstanceModel);
}