using Shared.Models;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Connections.RateLimiting;

namespace Server.Application.Coordinator.Connections;

public class ClientConnection : IClientConnection
{
    public event Action<string>? Disconnected;
    
    public string ConnectionId => _webSocketGateway.ConnectionId;

    public int? UserId
    {
        get => _webSocketGateway.UserId;
        set => _webSocketGateway.UserId = value;
    }

    public Guid? UserSessionId { get; set; }

    public string IpEndpoint => _webSocketGateway.IpAddress;
    public bool IsAvailable => _webSocketGateway.IsAvailable;
    public bool IsClosed => TokenSource.IsCancellationRequested;
    public CancellationTokenSource TokenSource { get; set; }
    public long LastHeartbeat { get; set; }
    public ConnectionRequestGuard RequestGuard { get; }
    public ConnectionHandlingQueue HandlingQueue { get; } = new();
    
    private readonly ICoordinatorInstanceGateway _webSocketGateway;
    
    public ClientConnection(ICoordinatorInstanceGateway webSocketGateway, 
        CancellationTokenSource tokenSource,
        RequestRateLimits? limits = null)
    {
        RequestGuard = new ConnectionRequestGuard(TimeProvider.System, limits);
        _webSocketGateway = webSocketGateway;
        TokenSource = tokenSource;
        LastHeartbeat = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _webSocketGateway.Disconnected += OnDisconnect;
    }

    public void Send(BaseMessage message)
    {
        _webSocketGateway.Send(message);
    }

    public void SendSerialized(byte[] utf8Json)
    {
        _webSocketGateway.SendSerialized(utf8Json);
    }

    public void Close(ConnectionCloseReason reason = ConnectionCloseReason.Normal)
    {
        _webSocketGateway.Close(reason);
        TokenSource.Cancel();
    }

    private void OnDisconnect()
    {
        Disconnected?.Invoke(ConnectionId);
    }

    public void Dispose()
    {
        TokenSource.Dispose();
        _webSocketGateway.Disconnected -= OnDisconnect;
        
    }
}