using Shared.Models;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Sessions.Hybrid;

namespace Server.Application.Coordinator.Messaging;

public interface IMessageSender : IDisposable
{
    void SendMessageByUserId(int userId, BaseMessage message);
    void SendMessageById(string connectionId, BaseMessage message);
    void BroadcastMessage(BaseMessage message);
    void BroadcastMessage(IEnumerable<int> userIds, BaseMessage message);
}