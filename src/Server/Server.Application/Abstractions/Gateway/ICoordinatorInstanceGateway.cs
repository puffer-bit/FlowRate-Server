using Shared.Models;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Sessions.Hybrid;

namespace Server.Application.Abstractions.Gateway;

public interface ICoordinatorInstanceGateway
{
    event Action Disconnected;
    
    string ConnectionId { get; }
    bool IsAvailable { get; }
    int? UserId { get; set; }
    string IpAddress { get; }
    
    void Send(BaseMessage message);
    void SendSerialized(byte[] utf8Json);
    void Close(ConnectionCloseReason reason = ConnectionCloseReason.Normal);
}