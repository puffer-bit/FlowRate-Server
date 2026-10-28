using Server.Application.Coordinator.Events;
using Shared.Models;
using Server.Application.Mappers;
using Shared.Models.Notifications.SessionInfo;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions;

namespace Server.Application.Coordinator.Notifications;

/// <summary>Tells clients what changed in hybrid sessions.</summary>
public class HybridSessionNotifier :
    IEventHandler<HybridSessionCreatedEvent>,
    IEventHandler<HybridSessionReconfiguredEvent>,
    IEventHandler<HybridSessionDeletedEvent>,
    IEventHandler<HybridSessionStateChangedEvent>,
    IEventHandler<HybridSessionSFUNodeChangedEvent>,
    IEventHandler<HybridSessionSFUNodeConnectedEvent>,
    IEventHandler<HybridSessionSFUNodeDisconnectedEvent>,
    IEventHandler<HybridSessionPeerStateChangedEvent>,
    IEventHandler<HybridSessionPeerJoinedEvent>,
    IEventHandler<HybridSessionPeerApprovedEvent>,
    IEventHandler<HybridSessionPeerRejectedEvent>,
    IEventHandler<HybridSessionPeerLeftEvent>,
    IEventHandler<HybridSessionPeerReplacedEvent>,
    IEventHandler<HybridSessionPeerKickedEvent>,
    IEventHandler<HybridSessionPeerBannedEvent>,
    IEventHandler<HybridSessionPeerStartedVoiceShareEvent>,
    IEventHandler<HybridSessionPeerStoppedVoiceShareEvent>,
    IEventHandler<HybridSessionPeerStartedScreenShareEvent>,
    IEventHandler<HybridSessionPeerStoppedScreenShareEvent>,
    IEventHandler<HybridSessionPeerMuteMicrophoneEvent>,
    IEventHandler<HybridSessionPeerUnmuteMicrophoneEvent>,
    IEventHandler<HybridSessionPeerMuteSoundEvent>,
    IEventHandler<HybridSessionPeerUnmuteSoundEvent>,
    IEventHandler<HybridSessionPeerStartedWatchingScreenShareEvent>,
    IEventHandler<HybridSessionPeerStoppedWatchingScreenShareEvent>,
    IEventHandler<HybridSessionPeerAfkEvent>,
    IEventHandler<HybridSessionPeerNotAfkEvent>,
    IEventHandler<HybridSessionPeerPokedEvent>
{
    public const string ReplacedReason = "Joined from another device";

    private readonly IMessageSender _messageSender;
    private readonly SessionVisibility _visibility;

    public HybridSessionNotifier(IMessageSender messageSender, SessionVisibility visibility)
    {
        _messageSender = messageSender;
        _visibility = visibility;
    }

    public Task HandleAsync(HybridSessionCreatedEvent evt, CancellationToken ct = default)
    {
        _visibility.Created(evt.Session);
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionReconfiguredEvent evt, CancellationToken ct = default)
    {
        _visibility.Reconfigured(evt.Session);
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionDeletedEvent evt, CancellationToken ct = default)
    {
        _visibility.Deleted(evt.SessionId, new HybridSessionDeletedNotification(evt.SessionId));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionStateChangedEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId, new HybridSessionStateChangedNotification(evt.SessionId, evt.State));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionSFUNodeChangedEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId, new HybridSessionSFUNodeChangedNotification(evt.SessionId));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionSFUNodeConnectedEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId, new HybridSessionSFUNodeConnectedNotification(evt.SessionId));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionSFUNodeDisconnectedEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId, new HybridSessionSFUNodeDisconnectedNotification(evt.SessionId));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionPeerStateChangedEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId, new HybridSessionPeerStateChangedNotification(evt.SessionId, evt.UserId, evt.State));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionPeerJoinedEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId, new HybridSessionPeerJoinedNotification(evt.SessionId, evt.Peer.ToDto()));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionPeerApprovedEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId, new HybridSessionPeerApprovedNotification(evt.SessionId, evt.UserId, evt.InitiatorUserId));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionPeerRejectedEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId, new HybridSessionPeerRejectedNotification(evt.SessionId, evt.UserId, evt.InitiatorUserId, evt.Reason));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionPeerLeftEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId, new HybridSessionPeerLeftNotification(evt.SessionId, evt.UserId));
        return Task.CompletedTask;
    }

    // Only the device the user left: to the others the user is still in the session
    public Task HandleAsync(HybridSessionPeerReplacedEvent evt, CancellationToken ct = default)
    {
        _messageSender.SendMessageById(evt.ReplacedConnectionId, new HybridSessionPeerKickedNotification(evt.SessionId,
            evt.UserId, null, ReplacedReason));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionPeerKickedEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId, new HybridSessionPeerKickedNotification(evt.SessionId, evt.UserId, evt.InitiatorUserId, evt.Reason));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionPeerBannedEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId, new HybridSessionPeerBannedNotification(evt.SessionId, evt.UserId, evt.InitiatorUserId, evt.Reason));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionPeerStartedVoiceShareEvent evt, CancellationToken ct = default)
    {
        SendToPeer(evt.ListenerUserId, evt.ListenerConnectionId,
            new HybridSessionPeerStartedVoiceShareNotification(evt.SessionId, evt.UserId, evt.Inbound));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionPeerStoppedVoiceShareEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId, new HybridSessionPeerStoppedVoiceShareNotification(evt.SessionId, evt.UserId, evt.InitiatorUserId));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionPeerStartedScreenShareEvent evt, CancellationToken ct = default)
    {
        var notification = new HybridSessionPeerStartedScreenShareNotification(evt.SessionId, evt.UserId, evt.Inbound,
            evt.HasAudio);

        if (evt.ListenerUserId is { } listenerUserId)
            _messageSender.SendMessageByUserId(listenerUserId, notification);
        else
            _visibility.Send(evt.SessionId, notification);

        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionPeerStoppedScreenShareEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId, new HybridSessionPeerStoppedScreenShareNotification(evt.SessionId, evt.UserId, evt.InitiatorUserId));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionPeerMuteMicrophoneEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId, new HybridSessionPeerMuteMicrophoneNotification(evt.SessionId, evt.UserId, evt.InitiatorUserId));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionPeerUnmuteMicrophoneEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId, new HybridSessionPeerUnmuteMicrophoneNotification(evt.SessionId, evt.UserId, evt.InitiatorUserId));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionPeerMuteSoundEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId, new HybridSessionPeerMuteSoundNotification(evt.SessionId, evt.UserId, evt.InitiatorUserId));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionPeerUnmuteSoundEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId, new HybridSessionPeerUnmuteSoundNotification(evt.SessionId, evt.UserId, evt.InitiatorUserId));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionPeerStartedWatchingScreenShareEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId,
            new HybridSessionPeerStartedWatchingScreenShareNotification(evt.SessionId, evt.UserId, evt.SharerUserId));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionPeerStoppedWatchingScreenShareEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId,
            new HybridSessionPeerStoppedWatchingScreenShareNotification(evt.SessionId, evt.UserId, evt.SharerUserId));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionPeerAfkEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId, new HybridSessionPeerAfkNotification(evt.SessionId, evt.UserId, evt.Reason));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HybridSessionPeerNotAfkEvent evt, CancellationToken ct = default)
    {
        _visibility.Send(evt.SessionId, new HybridSessionPeerNotAfkNotification(evt.SessionId, evt.UserId));
        return Task.CompletedTask;
    }

    // Only the poked user hears of a poke
    public Task HandleAsync(HybridSessionPeerPokedEvent evt, CancellationToken ct = default)
    {
        SendToPeer(evt.UserId, evt.ConnectionId,
            new HybridSessionPeerPokedNotification(evt.SessionId, evt.UserId, evt.InitiatorUserId, evt.Message));
        return Task.CompletedTask;
    }

    // What belongs to the media of a peer goes to the device the user takes part from, not to all their devices
    private void SendToPeer(int userId, string? connectionId, BaseMessage notification)
    {
        if (connectionId != null)
            _messageSender.SendMessageById(connectionId, notification);
        else
            _messageSender.SendMessageByUserId(userId, notification);
    }
}
