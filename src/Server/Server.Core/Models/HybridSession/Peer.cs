using System.Collections.Immutable;
using Server.Core.Enums;
using Shared.Enums;

namespace Server.Core.Models.HybridSession;

/// <summary>
/// A participant of a hybrid session. Owns the participant's lifecycle: admission by the host,
/// transport connection and microphone state. Media routing itself stays with the SFU.
/// </summary>
public sealed class Peer
{
    public int UserId { get; }
    /// <summary>The connection of the device the user takes part from; null when not known.</summary>
    public string? ConnectionId { get; private set; }
    public bool IsHost { get; }
    public PeerAdmission Admission { get; private set; }
    public HybridSessionPeerState State { get; private set; }
    public bool IsTransportConnected { get; private set; }
    public bool IsMicrophoneMuted { get; private set; }
    public bool IsSoundMuted { get; private set; }
    public bool IsScreenShareActive { get; private set; }
    public bool IsScreenAudioActive { get; private set; }

    /// <summary>The user stepped away from the keyboard; the reason they gave goes with it.</summary>
    public bool IsAfk { get; private set; }
    public string? AfkReason { get; private set; }

    /// <summary>The users whose screen share the peer watches; replaced whole, so it can be read without the session lock.</summary>
    public ImmutableHashSet<int> WatchedScreenShares { get; private set; } = ImmutableHashSet<int>.Empty;

    public bool IsApproved => Admission == PeerAdmission.Approved;
    public bool IsRejected => Admission == PeerAdmission.Rejected;
    public bool IsAdmitted => Admission is PeerAdmission.NotRequired or PeerAdmission.Approved;

    /// <summary>Only admitted peers with a connected transport may consume other peers' media.</summary>
    public bool CanReceiveMedia => State == HybridSessionPeerState.Connected;

    private Peer(int userId, bool isHost, PeerAdmission admission, string? connectionId)
    {
        UserId = userId;
        ConnectionId = connectionId;
        IsHost = isHost;
        Admission = admission;
        State = admission == PeerAdmission.Pending
            ? HybridSessionPeerState.WaitingForApprove
            : HybridSessionPeerState.Connecting;
    }

    public static Peer Join(int userId, HybridSessionModel session, string? connectionId = null)
    {
        if (session.IsHost(userId))
            return new Peer(userId, isHost: true, PeerAdmission.NotRequired, connectionId);

        return new Peer(userId, isHost: false,
            session.IsHostMustApprove ? PeerAdmission.Pending : PeerAdmission.NotRequired, connectionId);
    }

    /// <summary>
    /// The same user joins again, e.g. after a reconnect or from another device, which takes the peer over.
    /// A rejected peer stays rejected.
    /// </summary>
    public HybridSessionJoinResult Rejoin(string? connectionId = null)
    {
        if (IsRejected)
            return HybridSessionJoinResult.Rejected;

        ConnectionId = connectionId;
        IsTransportConnected = false;
        IsMicrophoneMuted = false;
        IsSoundMuted = false;
        IsScreenShareActive = false;
        IsScreenAudioActive = false;
        IsAfk = false;
        AfkReason = null;
        WatchedScreenShares = ImmutableHashSet<int>.Empty;
        State = IsAdmitted ? HybridSessionPeerState.Connecting : HybridSessionPeerState.WaitingForApprove;
        return HybridSessionJoinResult.NoError;
    }

    /// <summary>
    /// The SFU transport is up. A peer still waiting for approval keeps waiting and becomes
    /// <see cref="HybridSessionPeerState.Connected"/> only once approved.
    /// </summary>
    public HybridSessionConnectTransportResult MarkTransportConnected()
    {
        if (IsRejected)
            return HybridSessionConnectTransportResult.Rejected;

        IsTransportConnected = true;
        if (IsAdmitted)
            State = HybridSessionPeerState.Connected;
        return HybridSessionConnectTransportResult.NoError;
    }

    public ApproveUserSessionResult Approve()
    {
        if (IsHost)
            return ApproveUserSessionResult.InternalError;
        if (IsRejected)
            return ApproveUserSessionResult.AlreadyRejected;
        if (Admission != PeerAdmission.Pending)
            return ApproveUserSessionResult.NoError;

        Admission = PeerAdmission.Approved;
        State = IsTransportConnected ? HybridSessionPeerState.Connected : HybridSessionPeerState.Connecting;
        return ApproveUserSessionResult.NoError;
    }

    public RejectUserSessionResult Reject()
    {
        if (IsHost)
            return RejectUserSessionResult.InternalError;
        if (IsApproved)
            return RejectUserSessionResult.AlreadyApproved;

        Admission = PeerAdmission.Rejected;
        Close();
        return RejectUserSessionResult.NoError;
    }

    /// <summary>The SFU node was lost or replaced; the peer has to rejoin.</summary>
    public void WaitForServer()
    {
        if (State == HybridSessionPeerState.Closed)
            return;

        IsTransportConnected = false;
        IsMicrophoneMuted = false;
        IsSoundMuted = false;
        IsScreenShareActive = false;
        IsScreenAudioActive = false;
        IsAfk = false;
        AfkReason = null;
        WatchedScreenShares = ImmutableHashSet<int>.Empty;
        State = HybridSessionPeerState.WaitingForServer;
    }

    public void Close()
    {
        IsTransportConnected = false;
        IsMicrophoneMuted = false;
        IsSoundMuted = false;
        IsScreenShareActive = false;
        IsScreenAudioActive = false;
        IsAfk = false;
        AfkReason = null;
        WatchedScreenShares = ImmutableHashSet<int>.Empty;
        State = HybridSessionPeerState.Closed;
    }

    /// <returns>False if the microphone already was in the requested state.</returns>
    public bool SetMicrophoneMuted(bool isMuted)
    {
        if (IsMicrophoneMuted == isMuted)
            return false;

        IsMicrophoneMuted = isMuted;
        return true;
    }

    public bool SetSoundMuted(bool isMuted)
    {
        if (IsSoundMuted == isMuted)
            return false;

        IsSoundMuted = isMuted;
        return true;
    }

    /// <returns>False if the peer already watched the screen share of the user.</returns>
    public bool StartWatchingScreenShare(int userId) => Replace(WatchedScreenShares.Add(userId));

    /// <returns>False if the peer did not watch the screen share of the user.</returns>
    public bool StopWatchingScreenShare(int userId) => Replace(WatchedScreenShares.Remove(userId));

    public void ForgetWatchedScreenShares() => WatchedScreenShares = ImmutableHashSet<int>.Empty;

    private bool Replace(ImmutableHashSet<int> watched)
    {
        if (ReferenceEquals(watched, WatchedScreenShares))
            return false;

        WatchedScreenShares = watched;
        return true;
    }

    /// <returns>False if the peer already was in the requested state with the same reason.</returns>
    public bool SetAfk(bool isAfk, string? reason)
    {
        reason = isAfk && !string.IsNullOrWhiteSpace(reason) ? reason.Trim() : null;
        if (IsAfk == isAfk && AfkReason == reason)
            return false;

        IsAfk = isAfk;
        AfkReason = reason;
        return true;
    }

    /// <param name="hasAudio">The share carries the sound of the shared application or system; ignored when stopped.</param>
    public void SetScreenShareActive(bool isActive, bool hasAudio = false)
    {
        IsScreenShareActive = isActive;
        IsScreenAudioActive = isActive && hasAudio;
    }

    public PeerSnapshot ToSnapshot() =>
        new(UserId, State, IsTransportConnected, IsApproved, IsRejected, IsMicrophoneMuted, IsScreenShareActive, IsSoundMuted,
            IsScreenAudioActive, IsAfk, AfkReason, WatchedScreenShares);
}
