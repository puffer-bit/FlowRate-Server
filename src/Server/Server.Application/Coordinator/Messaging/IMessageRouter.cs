using Shared.Models;
using Shared.Models.Responses;
using Server.Application.Abstractions.Gateway;

namespace Server.Application.Coordinator.Messaging;

public interface IMessageRouter
{
    event Action<string, string> MessageReceived;
    event Action<string, ICoordinatorInstanceGateway> NewConnectionOpened;
    
    void AddConnection(ICoordinatorInstanceGateway clientGateway);
    void RouteIncomingMessage(BaseMessage? message, string connectionId);

    /// <summary>
    /// Counts a message that could not be parsed against the connection, like a rejected request, and sends
    /// <paramref name="rejection"/> when the message was a request the parser could still name.
    /// </summary>
    void RejectMalformedMessage(string connectionId, ErrorResponse? rejection = null);
}