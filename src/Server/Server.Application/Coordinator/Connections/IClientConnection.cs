using Shared.Models;
using Server.Application.Coordinator.Connections.RateLimiting;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Authentication;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Events;

namespace Server.Application.Coordinator.Connections;

public interface IClientConnection : IDisposable
{
    event Action<string>? Disconnected;
    
    string ConnectionId { get; }
    int? UserId { get; set; }
    string IpEndpoint { get; }
    bool IsAvailable { get; }
    /// <summary>The server closed the connection, or is closing it; nothing is attached to it any more.</summary>
    bool IsClosed { get; }
    CancellationTokenSource TokenSource { get; set; }
    long LastHeartbeat { get; set; }
    ConnectionRequestGuard RequestGuard { get; }
    ConnectionHandlingQueue HandlingQueue { get; }
    
    void Send(BaseMessage message);
    void SendSerialized(byte[] utf8Json);
    void Close(ConnectionCloseReason reason = ConnectionCloseReason.Normal);
}