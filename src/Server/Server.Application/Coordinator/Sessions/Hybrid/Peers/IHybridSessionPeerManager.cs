using Shared.Enums;
using Shared.Models.Media;
using Shared.Models.Media.Transport;
using Server.Core.Models.HybridSession;
using Server.Application.Abstractions.Sfu;
using Server.Application.Coordinator.Users;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Events;

namespace Server.Application.Coordinator.Sessions.Hybrid.Peers;

public interface IHybridSessionPeerManager : IDisposable
{
    bool IsReady { get; }

    /// <summary>The node the session runs on; null while no node of the pool is available.</summary>
    ISFUGateway? CurrentGateway { get; }

    /// <summary>
    /// Moves the session to another node; peers rejoin there. Null detaches the session from any node: it waits for a node,
    /// peers stay and rejoin by themselves once one is assigned.
    /// </summary>
    Task ChangeGatewayAsync(ISFUGateway? gateway);

    /// <param name="connectionId">
    /// The connection the user joins from. Joining from another connection than the peer has takes the peer over: the
    /// previous connection is out (<see cref="HybridSessionPeerReplacedEvent"/>).
    /// </param>
    Task<(HybridSessionJoinResult result, TransportData? transportData)> AddPeerAsync(int userId, string clientEpoch,
        string? connectionId = null);
    Task<HybridSessionConnectTransportResult> ConnectPeerAsync(int userId, DTLSParameters dtlsParameters);
    Task<ApproveUserSessionResult> ApprovePeerAsync(int initiatorId, int userId);
    Task<RejectUserSessionResult> RejectPeerAsync(int initiatorId, int userId, string? reason = null);
    Task<LeaveSessionResult> KickPeerAsync(int initiatorId, int userId, string? reason = null);
    Task<BanFromSessionResult> BanPeerAsync(int initiatorId, int userId, string? reason = null);
    Task<LeaveSessionResult> RemovePeerAsync(int userId);
    /// <summary>Removes the peer only while it takes part from this connection.</summary>
    Task<LeaveSessionResult> RemovePeerOfConnectionAsync(int userId, string connectionId);
    Task KickAllPeersAsync(string? reason = null);

    Task<(StartVoiceShareResult result, Outbound? outbound)> StartVoiceShareAsync(int userId);
    Task<StopVoiceShareResult> StopVoiceShareAsync(int initiatorId, int userId);
    Task<SetMicrophoneMuteResult> SetMicrophoneMutedAsync(int initiatorId, int userId, bool isMuted);
    Task<SetSoundMuteResult> SetSoundMutedAsync(int initiatorId, int userId, bool isMuted);
    Task<SetAfkResult> SetAfkAsync(int userId, bool isAfk, string? reason);
    Task<PokeResult> PokeAsync(int initiatorId, int userId, string? message);

    Task<(StartVideoShareResult result, Outbound? outbound)> StartVideoShareAsync(int userId);
    Task<StopVideoShareResult> StopVideoShareAsync(int initiatorId, int userId);
    /// <param name="withAudio">Also publish the sound of the shared application or system.</param>
    Task<(StartScreenShareResult result, Outbound? outbound, Outbound? audioOutbound)> StartScreenShareAsync(int userId,
        bool withAudio);
    Task<StopScreenShareResult> StopScreenShareAsync(int initiatorId, int userId);
    /// <returns>The inbound of the picture and, when the share carries sound, the inbound of the sound.</returns>
    Task<(SetScreenShareWatchResult result, Inbound? inbound, Inbound? audioInbound)> SetScreenShareWatchAsync(
        int listenerUserId, int userId, bool isWatching);
    Task<ResumeInboundsResult> ResumeInboundsAsync(int listenerUserId, IReadOnlyCollection<string> inboundIds);

    bool HasPeer(int userId);
    /// <summary>
    /// False only when the user is a peer from another connection: the media of the peer is not this connection's.
    /// </summary>
    bool IsPeerOf(int userId, string connectionId);
    IReadOnlyDictionary<int, PeerSnapshot> GetAllPeerSnapshots();
}
