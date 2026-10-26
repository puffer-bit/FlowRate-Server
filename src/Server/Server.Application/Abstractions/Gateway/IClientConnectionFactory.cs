using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Sessions.Hybrid;



namespace Server.Application.Abstractions.Gateway;

public interface IClientConnectionFactory
{
    IClientConnection CreateClientConnection(ICoordinatorInstanceGateway webSocket);
}