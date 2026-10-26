using System.Collections.Concurrent;
using Server.Application.Abstractions.Sfu;
using Server.Application.Coordinator.Heartbeat;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Users;
using Server.Application.Coordinator.Authentication;

namespace Server.Application.Coordinator.Connections;

public interface IConnectionsContext : IDisposable
{
    ConcurrentDictionary<string, IClientConnection> ClientConnections { get; }

    /// <summary>
    /// The connections each attached user is on. Written by the user manager alone, under its attach lock; each
    /// value is replaced, never changed, so readers can take it without a lock.
    /// </summary>
    ConcurrentDictionary<int, string[]> UserConnections { get; }
}