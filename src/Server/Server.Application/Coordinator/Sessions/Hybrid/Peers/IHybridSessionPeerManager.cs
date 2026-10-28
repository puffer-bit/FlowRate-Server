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
    Task<(ErrorCode? result, TransportData? transportData)> AddPeerAsync(int userId, string clientEpoch,
        string? connectionId = null);
    Task<ErrorCode?> ConnectPeerAsync(int userId, DTLSParameters dtlsParameters);
    Task<ErrorCode?> ApprovePeerAsync(int initiatorId, int userId);
    Task<ErrorCode?> RejectPeerAsync(int initiatorId, int userId, string? reason = null);
    Task<ErrorCode?> KickPeerAsync(int initiatorId, int userId, string? reason = null);
    Task<ErrorCode?> BanPeerAsync(int initiatorId, int userId, string? reason = null);
    Task<ErrorCode?> RemovePeerAsync(int userId);
    /// <summary>Removes the peer only while it takes part from this connection.</summary>
    Task<ErrorCode?> RemovePeerOfConnectionAsync(int userId, string connectionId);
    Task KickAllPeersAsync(string? reason = null);

    Task<(ErrorCode? result, Outbound? outbound)> StartVoiceShareAsync(int userId);
    Task<ErrorCode?> StopVoiceShareAsync(int initiatorId, int userId);
    Task<ErrorCode?> SetMicrophoneMutedAsync(int initiatorId, int userId, bool isMuted);
    Task<ErrorCode?> SetSoundMutedAsync(int initiatorId, int userId, bool isMuted);
    Task<ErrorCode?> SetAfkAsync(int userId, bool isAfk, string? reason);
    Task<ErrorCode?> PokeAsync(int initiatorId, int userId, string? message);

    Task<(ErrorCode? result, Outbound? outbound)> StartVideoShareAsync(int userId);
    Task<ErrorCode?> StopVideoShareAsync(int initiatorId, int userId);
    /// <param name="withAudio">Also publish the sound of the shared application or system.</param>
    Task<(ErrorCode? result, Outbound? outbound, Outbound? audioOutbound)> StartScreenShareAsync(int userId,
        bool withAudio);
    Task<ErrorCode?> StopScreenShareAsync(int initiatorId, int userId);
    /// <returns>The inbound of the picture and, when the share carries sound, the inbound of the sound.</returns>
    Task<(ErrorCode? result, Inbound? inbound, Inbound? audioInbound)> SetScreenShareWatchAsync(
        int listenerUserId, int userId, bool isWatching);
    Task<ErrorCode?> ResumeInboundsAsync(int listenerUserId, IReadOnlyCollection<string> inboundIds);

    bool HasPeer(int userId);
    /// <summary>
    /// False only when the user is a peer from another connection: the media of the peer is not this connection's.
    /// </summary>
    bool IsPeerOf(int userId, string connectionId);
    IReadOnlyDictionary<int, PeerSnapshot> GetAllPeerSnapshots();
}
