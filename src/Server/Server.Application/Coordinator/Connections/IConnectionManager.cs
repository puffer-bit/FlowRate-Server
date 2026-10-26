using System.Diagnostics.CodeAnalysis;
using Server.Application.Coordinator.Heartbeat;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Abstractions.Gateway;


namespace Server.Application.Coordinator.Connections;

public interface IConnectionManager : IDisposable
{
    event Action<string>? ConnectionClosed;
    
    void AddConnection(ICoordinatorInstanceGateway webGateway);
    bool TryRemoveConnectionById(string connectionId, [NotNullWhen(true)] out IClientConnection? connection);

    bool TryGetConnectionById(string connectionId, [NotNullWhen(true)] out IClientConnection? connection);
    /// <summary>The open connections of a user, one per device they are connected from.</summary>
    IReadOnlyList<IClientConnection> GetUserConnections(int userId);

    void CloseAllConnections();
}