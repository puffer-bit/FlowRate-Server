using Shared.Enums;
using Shared.Models.Media;
using Server.Core.Models.HybridSession;

namespace Server.Application.Coordinator.Events;

/// <summary>Something changed inside one hybrid session.</summary>
public abstract record HybridSessionUpdatedEvent(int SessionId);

public record HybridSessionStateChangedEvent(int SessionId, HybridSessionNodeState State)
    : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionSFUNodeChangedEvent(int SessionId) : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionSFUNodeDisconnectedEvent(int SessionId) : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionSFUNodeConnectedEvent(int SessionId) : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionPeerJoinedEvent(int SessionId, PeerSnapshot Peer) : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionPeerStateChangedEvent(int SessionId, int UserId, HybridSessionPeerState State)
    : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionPeerLeftEvent(int SessionId, int UserId) : HybridSessionUpdatedEvent(SessionId);

/// <summary>The user joined again from another connection, which took the peer and its media over.</summary>
public record HybridSessionPeerReplacedEvent(int SessionId, int UserId, string ReplacedConnectionId)
    : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionPeerKickedEvent(int SessionId, int UserId, int? InitiatorUserId = null, string? Reason = null)
    : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionPeerBannedEvent(int SessionId, int UserId, int? InitiatorUserId = null, string? Reason = null)
    : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionPeerApprovedEvent(int SessionId, int UserId, int InitiatorUserId)
    : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionPeerRejectedEvent(int SessionId, int UserId, int? InitiatorUserId = null, string? Reason = null)
    : HybridSessionUpdatedEvent(SessionId);

/// <summary>Not sent to clients yet: video is not routed to the other peers.</summary>
public record HybridSessionPeerStartedVideoShareEvent(int SessionId, int UserId, Outbound Outbound)
    : HybridSessionUpdatedEvent(SessionId);

/// <summary>A listener started to receive the voice of a peer, on the connection they take part from.</summary>
public record HybridSessionPeerStartedVoiceShareEvent(int SessionId, int UserId, int ListenerUserId, Inbound Inbound,
    string? ListenerConnectionId = null) : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionPeerStoppedVoiceShareEvent(int SessionId, int UserId, int? InitiatorUserId = null)
    : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionPeerStartedScreenShareEvent(int SessionId, int UserId, int? ListenerUserId = null,
    Inbound? Inbound = null, bool HasAudio = false) : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionPeerStoppedScreenShareEvent(int SessionId, int UserId, int? InitiatorUserId = null)
    : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionPeerMuteMicrophoneEvent(int SessionId, int UserId, int? InitiatorUserId = null)
    : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionPeerUnmuteMicrophoneEvent(int SessionId, int UserId, int? InitiatorUserId = null)
    : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionPeerMuteSoundEvent(int SessionId, int UserId, int? InitiatorUserId = null)
    : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionPeerUnmuteSoundEvent(int SessionId, int UserId, int? InitiatorUserId = null)
    : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionPeerStartedWatchingScreenShareEvent(int SessionId, int UserId, int SharerUserId)
    : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionPeerStoppedWatchingScreenShareEvent(int SessionId, int UserId, int SharerUserId)
    : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionPeerAfkEvent(int SessionId, int UserId, string? Reason)
    : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionPeerNotAfkEvent(int SessionId, int UserId)
    : HybridSessionUpdatedEvent(SessionId);

public record HybridSessionPeerPokedEvent(int SessionId, int UserId, int InitiatorUserId, string? Message,
    string? ConnectionId = null) : HybridSessionUpdatedEvent(SessionId);
