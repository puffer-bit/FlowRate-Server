using Shared.Enums;
using Shared.Models.Media;
using Server.Core.Models.HybridSession;
using Shared.Models.Media.Transport;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Users;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator;
using Server.Application.Coordinator.Events;

namespace Server.Application.Abstractions.Sfu;

/// <summary>
/// Connection to one SFU node. Every call has a deadline: a node that does not answer in time
/// makes the call throw <see cref="TimeoutException"/>.
/// </summary>
public interface ISFUGateway
{
    int NodeId { get; }
    
    event Func<Task>? ConnectionLost;
    event Func<Task>? Connected;
    
    bool IsConnected { get; }

    /// <summary>Connection state and ping of the node, refreshed by a periodic ping.</summary>
    SfuNodeStatus Status { get; }

    /// <summary>Raised when <see cref="Status"/> changed its state (not on every new round trip).</summary>
    event Action? StatusChanged;
    
    Task Connect();
    Task Disconnect();
    
    Task<(SFUTransportCreateResult result, TransportData? transportData)> CreatePeerTransport(int session, int userId,
        string clientEpoch);

    Task<SFUTransportConnectResult> ConnectPeerTransport(int sessionId,
        int userId,
        DTLSParameters dtlsParameters);
    Task ClosePeerTransport(int sessionId, int userId);
    Task<(SFUOutboundCreateResult result, Outbound? outbound)> AddOutbound(int sessionId, int userId,
        MediaSourceType sourceType, MediaType mediaType, OutboundMediaSettings settings);
    Task<SFUOutboundCreateResult> PauseOutbound(int sessionId, string outboundId);
    Task<SFUOutboundCreateResult> ResumeOutbound(int sessionId, string outboundId);
    Task<SFUOutboundCreateResult> UpdateOutbound(int sessionId, int userId, Outbound outbound);
    Task<SFUOutboundCreateResult> RemoveOutbound(int sessionId, int userId, string outboundId);
    Task<SFUOutboundDowngradeResult> DowngradeOutbound(int sessionId, int userId, MediaSourceType sourceType,
        MediaType? type = null);
    Task<SFUOutboundUpgradeResult> UpgradeOutbound(int sessionId, int userId, MediaSourceType sourceType,
        MediaType? type = null);
    
    Task<(SFUInboundCreateResult result, Inbound? inbound)> AddInbound(int sessionId, int userId, Outbound outbound,
        InboundMediaSettings settings);
    Task<SFUInboundCreateResult> RemoveInbound(int sessionId, int userId, string inboundId);
    Task<SFUInboundCreateResult> PauseInbound(int sessionId, string inboundId);
    Task<SFUInboundCreateResult> ResumeInbound(int sessionId, string inboundId);
    
    Task<List<Inbound>> GetAllInbounds(int sessionId, int? userId);

    Task LoadSession(int sessionId);
    Task RestartSession(int sessionId);
    Task UnloadSession(int sessionId);
}