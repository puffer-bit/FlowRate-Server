using Microsoft.Extensions.Logging;
using Shared.Models;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Authentication;

namespace Server.Application.Coordinator.Messaging;

public class MessageSender : IMessageSender
{
    private readonly IConnectionsContext _connectionsContext;
    private readonly IMessageSerializer _serializer;
    private readonly ILogger _logger;
    
    public MessageSender(
        IConnectionsContext connectionsContext,
        IMessageSerializer serializer,
        ILogger logger)
    {
        _logger = logger;
        _serializer = serializer;
        _connectionsContext = connectionsContext;
    }

    public void SendMessageByUserId(int userId, BaseMessage message)
    {
        byte[]? utf8Json = null;
        SendToUser(userId, message, ref utf8Json);
    }
    
    public void SendMessageById(string connectionId, BaseMessage message)
    {
        if (_connectionsContext.ClientConnections.TryGetValue(connectionId, out var connection))
        {
            connection.Send(message);
        }
    }
    
    public void BroadcastMessage(BaseMessage message)
    {
        byte[]? utf8Json = null;
        foreach (var connection in _connectionsContext.ClientConnections.Values)
        {
            if (!connection.TokenSource.IsCancellationRequested && 
                connection.UserId.HasValue)
            {
                if (!TrySerialize(message, ref utf8Json))
                    return;
                connection.SendSerialized(utf8Json!);
            }
        }
    }
    
    public void BroadcastMessage(IEnumerable<int> userIds, BaseMessage message)
    {
        byte[]? utf8Json = null;
        foreach (var userId in userIds)
        {
            if (!SendToUser(userId, message, ref utf8Json))
                return;
        }
    }

    /// <summary>Sends to every device the user is connected from.</summary>
    /// <returns>False when the message cannot be serialized.</returns>
    private bool SendToUser(int userId, BaseMessage message, ref byte[]? utf8Json)
    {
        if (!_connectionsContext.UserConnections.TryGetValue(userId, out var connectionIds))
            return true;

        foreach (var connectionId in connectionIds)
        {
            if (!_connectionsContext.ClientConnections.TryGetValue(connectionId, out var connection)
                || connection.TokenSource.IsCancellationRequested || !connection.UserId.HasValue)
                continue;

            if (!TrySerialize(message, ref utf8Json))
                return false;
            connection.SendSerialized(utf8Json!);
        }

        return true;
    }

    private bool TrySerialize(BaseMessage message, ref byte[]? utf8Json)
    {
        if (utf8Json != null)
            return true;

        try
        {
            utf8Json = _serializer.Serialize(message);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize broadcast {MessageType}.", message.GetType().Name);
            return false;
        }
    }
    
    public void Dispose()
    {
        
    }
}