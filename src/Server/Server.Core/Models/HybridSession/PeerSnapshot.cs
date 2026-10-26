using Shared.Enums;

namespace Server.Core.Models.HybridSession;

/// <summary>Immutable copy of a <see cref="Peer"/> taken at one moment, safe to pass to events and responses.</summary>
public sealed record PeerSnapshot(
    int UserId,
    HybridSessionPeerState State,
    bool IsTransportConnected,
    bool IsApproved,
    bool IsRejected,
    bool IsMicrophoneMuted,
    bool IsScreenShareActive = false,
    bool IsSoundMuted = false,
    bool IsScreenAudioActive = false,
    bool IsAfk = false,
    string? AfkReason = null);
