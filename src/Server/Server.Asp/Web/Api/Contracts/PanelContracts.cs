using System.ComponentModel.DataAnnotations;
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

/// <summary>Creates and starts a coordinator using standard heartbeat, media and rate limit settings.</summary>
public sealed class CreateCoordinatorDto
{
    [Required] public string Name { get; init; } = string.Empty;
    [Required] public string Ip { get; init; } = "0.0.0.0";
    public int Port { get; init; }
    public int MaxOnlineUsers { get; init; } = 64;
    public int MaxUsers { get; init; } = 256;
    [Required] public IReadOnlyList<int> SfuNodeIds { get; init; } = [];
}

/// <summary>The configuration was saved. Failed means its listener could not start; edit its settings to retry.</summary>
public sealed record CoordinatorCreatedDto(string Id, CoordinatorState State);
