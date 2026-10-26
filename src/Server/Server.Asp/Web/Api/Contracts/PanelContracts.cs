using Server.Core.Enums;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Enums.WebRTC;

namespace Server.Asp.Web.Api.Contracts;

public sealed record PanelOverviewDto(
    string ServerVersion,
    DateTime StartedAtUtc,
    int ActiveSessionsCount,
    int ConnectedUsersCount,
    IReadOnlyList<CoordinatorSummaryDto> Coordinators);

public sealed record CoordinatorSummaryDto(
    string Id,
    string Name,
    string Ip,
    int Port,
    CoordinatorState State,
    DateTime StartedAtUtc,
    int SessionsCount,
    int ActiveSessionsCount,
    int ConnectedUsersCount);

public sealed record CoordinatorDetailsDto(
    CoordinatorSummaryDto Summary,
    IReadOnlyList<HybridSessionDto> Sessions,
    IReadOnlyList<ConnectedUserDto> Users);

/// <param name="Capacity">Null when the session has no capacity limit.</param>
public sealed record HybridSessionDto(
    int Id,
    string Name,
    int HostId,
    int? Capacity,
    WebRTCEngine EngineType,
    bool IsHostMustApprove,
    HybridSessionNodeState State,
    IReadOnlyList<PeerDto> Peers);

public sealed record PeerDto(
    int UserId,
    HybridSessionPeerState State,
    bool IsTransportConnected,
    bool IsApproved,
    bool IsMicrophoneMuted,
    bool IsSoundMuted);

public sealed record ConnectedUserDto(
    int Id,
    string Username,
    string? DisplayName,
    UserStatus Status,
    DateTime ConnectedAtUtc);
