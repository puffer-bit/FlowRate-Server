using Server.Application.Coordinator.Snapshots;
using Server.Asp.Web.Api.Contracts;
using Server.Core.Models;

namespace Server.Asp.Web.Api;

public static class PanelContractsMapper
{
    public static CoordinatorSummaryDto ToSummaryDto(this CoordinatorSnapshot snapshot) => new(
        snapshot.Id,
        snapshot.Name,
        snapshot.Ip,
        snapshot.Port,
        snapshot.State,
        snapshot.StartedAtUtc,
        snapshot.HybridSessions.Count,
        snapshot.ActiveSessionsCount,
        snapshot.ConnectedUsers.Count);

    public static CoordinatorDetailsDto ToDetailsDto(this CoordinatorSnapshot snapshot) => new(
        snapshot.ToSummaryDto(),
        snapshot.HybridSessions.Select(ToDto).ToList(),
        snapshot.ConnectedUsers.Select(ToDto).ToList());

    private static HybridSessionDto ToDto(HybridSessionSnapshot session) => new(
        session.Id,
        session.Name,
        session.HostId,
        session.Capacity == SessionModel.NoCapacityLimit ? null : session.Capacity,
        session.EngineType,
        session.IsHostMustApprove,
        session.State,
        session.Peers
            .Select(peer => new PeerDto(
                peer.UserId, peer.State, peer.IsTransportConnected, peer.IsApproved, peer.IsMicrophoneMuted, peer.IsSoundMuted))
            .ToList());

    private static ConnectedUserDto ToDto(ConnectedUserSnapshot user) => new(
        user.Id, user.Username, user.DisplayName, user.Status, user.LastConnectionTime);
}
