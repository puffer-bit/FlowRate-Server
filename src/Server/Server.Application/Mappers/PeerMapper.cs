using Shared.Models.DataTransferObjects;
using Server.Core.Models.HybridSession;

namespace Server.Application.Mappers;

public static class PeerMapperExtensions
{
    public static PeerDTO ToDto(this PeerSnapshot peer)
    {
        ArgumentNullException.ThrowIfNull(peer);

        return new PeerDTO
        {
            UserId = peer.UserId,
            State = peer.State,
            IsNegotiated = peer.IsTransportConnected,
            IsConnected = peer.IsTransportConnected,
            IsApproved = peer.IsApproved,
            IsRejected = peer.IsRejected,
            IsMicrophoneMuted = peer.IsMicrophoneMuted,
            IsSoundMuted = peer.IsSoundMuted,
            IsScreenShareActive = peer.IsScreenShareActive,
            IsScreenAudioActive = peer.IsScreenAudioActive,
            IsAfk = peer.IsAfk,
            AfkMessage = peer.AfkReason,
            WatchedScreenShares = peer.WatchedScreenShares?.ToList() ?? []
        };
    }
}
