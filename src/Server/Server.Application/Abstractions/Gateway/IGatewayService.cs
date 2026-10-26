using Server.Application.Coordinator.Messaging;
using Server.Application.Abstractions.Sfu;
using Server.Application.Coordinator.Heartbeat;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Events;


namespace Server.Application.Abstractions.Gateway;

public interface IGatewayService : IDisposable
{
    Task StartAsync(IMessageRouter messageRouter);
    Task StopAsync();
}