using Shared.Models;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Messaging;

namespace Server.Infrastructure.Tests.Coordinator.Connection.Gateway;

public sealed class FakeRouter : IMessageRouter
{
    public event Action<string, string>? MessageReceived;
    public event Action<string, ICoordinatorInstanceGateway>? NewConnectionOpened;

    private readonly List<ICoordinatorInstanceGateway> _gateways = new();
    private TaskCompletionSource<ICoordinatorInstanceGateway> _nextConnection = NewSource<ICoordinatorInstanceGateway>();
    private int _disconnectCount;
    private int _malformedCount;

    public TaskCompletionSource FirstDisconnect { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int DisconnectCount => Volatile.Read(ref _disconnectCount);
    public int MalformedCount => Volatile.Read(ref _malformedCount);
    public Action<BaseMessage, ICoordinatorInstanceGateway>? OnMessage { get; set; }

    public Task<ICoordinatorInstanceGateway> NextConnection
    {
        get { lock (_gateways) return _nextConnection.Task; }
    }

    public void AddConnection(ICoordinatorInstanceGateway gateway)
    {
        gateway.Disconnected += () =>
        {
            Interlocked.Increment(ref _disconnectCount);
            FirstDisconnect.TrySetResult();
        };

        lock (_gateways)
        {
            _gateways.Add(gateway);
            _nextConnection.TrySetResult(gateway);
            _nextConnection = NewSource<ICoordinatorInstanceGateway>();
        }
    }

    public void RouteIncomingMessage(BaseMessage? message, string connectionId)
    {
        ICoordinatorInstanceGateway gateway;
        lock (_gateways)
            gateway = _gateways.Single(g => g.ConnectionId == connectionId);

        if (message != null)
            OnMessage?.Invoke(message, gateway);
    }

    public void RejectMalformedMessage(string connectionId) => Interlocked.Increment(ref _malformedCount);

    private static TaskCompletionSource<T> NewSource<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
