using Server.Application.Coordinator.Events;
using Server.Application.Mappers;
using Shared.Models.Notifications.SessionInfo;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions;

namespace Server.Application.Coordinator.Notifications;

/// <summary>Tells clients what changed in chats.</summary>
public class ChatSessionNotifier :
    IEventHandler<ChatSessionCreatedEvent>,
    IEventHandler<ChatSessionReconfiguredEvent>,
    IEventHandler<ChatSessionDeletedEvent>,
    IEventHandler<ChatSessionMessageAddedEvent>,
    IEventHandler<ChatSessionMessageEditedEvent>,
    IEventHandler<ChatSessionMessageDeletedEvent>,
    IEventHandler<ChatSessionTypingEvent>
{
    private readonly IMessageSender _messageSender;
    private readonly SessionVisibility _visibility;

    public ChatSessionNotifier(IMessageSender messageSender, SessionVisibility visibility)
    {
        _messageSender = messageSender;
        _visibility = visibility;
    }

    public Task HandleAsync(ChatSessionCreatedEvent evt, CancellationToken ct = default)
    {
        _visibility.Created(evt.Session);
        return Task.CompletedTask;
    }

    public Task HandleAsync(ChatSessionReconfiguredEvent evt, CancellationToken ct = default)
    {
        _visibility.Reconfigured(evt.Session);
        return Task.CompletedTask;
    }

    public Task HandleAsync(ChatSessionDeletedEvent evt, CancellationToken ct = default)
    {
        _visibility.Deleted(evt.SessionId, new ChatSessionDeletedNotification(evt.SessionId));
        return Task.CompletedTask;
    }

    public Task HandleAsync(ChatSessionMessageAddedEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.ChatId, new ChatSessionMessageAddedNotification(evt.ChatId, evt.Message.ToDto()));
        return Task.CompletedTask;
    }

    public Task HandleAsync(ChatSessionMessageEditedEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.ChatId, new ChatSessionMessageEditedNotification(evt.ChatId, evt.Message.ToDto()));
        return Task.CompletedTask;
    }

    public Task HandleAsync(ChatSessionMessageDeletedEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.ChatId, new ChatSessionMessageDeletedNotification(evt.ChatId, evt.MessageId, evt.Revision));
        return Task.CompletedTask;
    }

    public Task HandleAsync(ChatSessionTypingEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.ChatId, new ChatSessionUserTypingNotification(evt.ChatId, evt.UserId));
        return Task.CompletedTask;
    }
}
