using System.Collections.Concurrent;
using Server.Application.Coordinator.Users;
using Server.Application.Coordinator.Authentication;

namespace Server.Application.Coordinator.Connections {
    public class ConnectionsContext : IConnectionsContext
    {
        public ConcurrentDictionary<string, IClientConnection> ClientConnections { get; } = new();
        public ConcurrentDictionary<int, string[]> UserConnections { get; } = new();
        
        public void Dispose()
        {
            foreach (var clientConnection in ClientConnections.Values)
            {
                clientConnection.Dispose();
            }
            ClientConnections.Clear();
            UserConnections.Clear();
        }
    }
}
