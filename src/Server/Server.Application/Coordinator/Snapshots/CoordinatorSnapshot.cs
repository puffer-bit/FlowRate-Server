using Server.Core.Enums;
using Server.Core.Models.HybridSession;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Enums.WebRTC;

namespace Server.Application.Coordinator.Snapshots;

/// <summary>Read-only copy of a running coordinator taken at one moment, for monitoring.</summary>
public sealed record CoordinatorSnapshot(
    string Id,
    string Name,
    string Ip,
    int Port,
    CoordinatorState State,
    DateTime StartedAtUtc,
    IReadOnlyList<HybridSessionSnapshot> HybridSessions,
    IReadOnlyList<ConnectedUserSnapshot> ConnectedUsers)
{
    /// <summary>A session is active while at least one peer is in it.</summary>
    public int ActiveSessionsCount => HybridSessions.Count(session => session.Peers.Count > 0);
}

public sealed record HybridSessionSnapshot(
    int Id,
    string Name,
    int HostId,
    int Capacity,
    WebRTCEngine EngineType,
    bool IsHostMustApprove,
    HybridSessionNodeState State,
    IReadOnlyList<PeerSnapshot> Peers);

public sealed record ConnectedUserSnapshot(
    int Id,
    string Username,
    string? DisplayName,
    UserStatus Status,
    DateTime LastConnectionTime);
