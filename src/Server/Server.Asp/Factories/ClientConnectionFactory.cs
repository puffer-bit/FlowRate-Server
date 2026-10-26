using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Connections.RateLimiting;

namespace Server.Asp.Factories;

public class ClientConnectionFactory : IClientConnectionFactory
{
    private readonly RequestRateLimits _limits;

    public ClientConnectionFactory(RequestRateLimits limits)
    {
        _limits = limits;
    }

    public IClientConnection CreateClientConnection(ICoordinatorInstanceGateway webSocket) =>
        new ClientConnection(webSocket, new CancellationTokenSource(), _limits);
}
