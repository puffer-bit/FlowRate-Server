using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Heartbeat;

namespace Server.Application.Coordinator.Connections {
    public class ConnectionManager : IConnectionManager
    {
        public static readonly TimeSpan AuthenticationTimeout = TimeSpan.FromSeconds(10);

        public event Action<string>? ConnectionClosed;
        
        private readonly ILogger _logger;
        private readonly IConnectionsContext _context;
        private readonly IHeartbeatManager _heartbeatManager;
        private readonly IClientConnectionFactory _clientConnectionFactory;
    
        public ConnectionManager(IConnectionsContext context, 
            IClientConnectionFactory clientConnectionFactory, 
            IHeartbeatManager heartbeatManager,
            ILogger logger)
        {
            _logger = logger;
            _heartbeatManager = heartbeatManager;
            _context = context;
            _clientConnectionFactory = clientConnectionFactory;
        }

        public void AddConnection(ICoordinatorInstanceGateway gateway)
        {
            var newClientConnection = _clientConnectionFactory.CreateClientConnection(gateway);
            
            _context.ClientConnections[gateway.ConnectionId] = newClientConnection;
            newClientConnection.Disconnected += OnConnectionDisconnected;
            _heartbeatManager.StartAsync(newClientConnection.ConnectionId);
            _ = CloseIfNotAuthenticatedAsync(newClientConnection);
        }

        private async Task CloseIfNotAuthenticatedAsync(IClientConnection connection)
        {
            try
            {
                await Task.Delay(AuthenticationTimeout, connection.TokenSource.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }

            if (connection.UserId != null
                || !_context.ClientConnections.TryGetValue(connection.ConnectionId, out var current)
                || !ReferenceEquals(current, connection))
                return;

            _logger.LogWarning("Connection {ConnectionId} from {Ip} did not authenticate within {Timeout}. Closing it.",
                connection.ConnectionId, connection.IpEndpoint, AuthenticationTimeout);
            connection.Close(ConnectionCloseReason.AuthenticationTimeout);
        }
        
        public bool TryGetConnectionById(string connectionId, 
            [NotNullWhen(true)] out IClientConnection? connection)
        {
            return _context.ClientConnections.TryGetValue(connectionId, out connection);
        }
        
        public IReadOnlyList<IClientConnection> GetUserConnections(int userId)
        {
            if (!_context.UserConnections.TryGetValue(userId, out var connectionIds))
                return [];

            var connections = new List<IClientConnection>(connectionIds.Length);
            foreach (var connectionId in connectionIds)
            {
                if (_context.ClientConnections.TryGetValue(connectionId, out var connection))
                    connections.Add(connection);
            }

            return connections;
        }
        
        public bool TryRemoveConnectionById(string connectionId, 
            [NotNullWhen(true)] out IClientConnection? connection)
        {
            // The user of the connection is detached by the user manager, which keeps the connections of users
            if (_context.ClientConnections.TryRemove(connectionId, out var clientConnection))
            {
                clientConnection.Disconnected -= OnConnectionDisconnected;
                clientConnection.Close();
                clientConnection.Dispose();
                connection = clientConnection;
                return true;
            }
            
            connection = null;
            return false;
        }
        
        public void CloseAllConnections()
        {
            foreach (var connection in _context.ClientConnections.Values)
            {
                connection.Disconnected -= OnConnectionDisconnected;
                connection.Close();
            }
            
            _context.Dispose();
        }

        private void OnConnectionDisconnected(string connectionId)
        {
            ConnectionClosed?.Invoke(connectionId);
        }
        
        public void Dispose()
        {
            foreach (var connection in _context.ClientConnections.Values)
            {
                connection.Disconnected -= OnConnectionDisconnected;
                connection.Close();
            }

            ConnectionClosed = null;
            
            _context.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
