using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Server.Api.Grpc;
using Server.Infrastructure.Mediasoup;
using Shared.Enums;
using Shared.Enums.WebRTC;
using Shared.Models.Media;
using Server.Core.Models.HybridSession;
using Shared.Models.Media.Transport;
using DTLSParameters = Shared.Models.Media.Transport.DTLSParameters;
using Server.Application.Orchestrator.SFUPool.Models;
using Server.Application.Orchestrator;
using Server.Core.Models.Orchestrator;
using Server.Application.Abstractions.Sfu;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Users;

namespace Server.Infrastructure.Coordinator.Services.Mediasoup;

public partial class MediasoupGateway : ISFUGateway
{
    public int NodeId { get; init; }

    private SFUEndpoint? _endpoint;
    
    private MediasoupServer.MediasoupServerClient? _grpcClient;
    private GrpcChannel? _activeChannel;
    private CancellationTokenSource? _cts;
    
    private readonly ILogger _logger;
    private readonly SfuConnectionSettings _connectionOptions;

    public MediasoupGateway(
        ILogger logger,
        SFUNodeModel model,
        IOrchestratorInstanceContext orchestrator)
    {
        NodeId = model.Id;
        _logger = logger;
        _endpoint = model.Endpoint;
        _connectionOptions = orchestrator.SfuConnection;
    }
    
    public async Task<(SFUTransportCreateResult result, TransportData? transportData)> CreatePeerTransport(int sessionId, int userId,
        string clientEpoch)
    {
        var request = new CreateTransportRequest 
        { 
            SessionId = sessionId, 
            UserId = userId,
            ClientEpoch = clientEpoch
        };
        var response = await CallAsync("CreatePeerTransport", (client, options) => client.CreatePeerTransportAsync(request, options));

        if (response.Status != ProtoSFUResultStatus.Success)
        {
            _logger.LogError("Error creating peer transport for user {UserId} in session {SessionId}. " +
                                 "Error message: {message}", userId, sessionId, response.ErrorMessage);
            return (SFUTransportCreateResult.SFUNodeError, null);
        }

        var transportData = new TransportData(
            Host: response.ServerHostAddress,
            Port: (ushort)response.ServerPort,
            DTLSData: MapProtoDtlsParametersToShared(response.DtlsParameters),
            ICEData: MapProtoIceParametersToShared(response.IceParameters)
        );

        return (SFUTransportCreateResult.NoError, transportData);
    }
    
    public async Task<SFUTransportConnectResult> ConnectPeerTransport(int sessionId, int userId,
        DTLSParameters dtlsParameters)
    {
        var request = new ConnectTransportRequest() 
        { 
            SessionId = sessionId, 
            UserId = userId,
            ClientDtls = MapSharedDtlsParametersToProto(dtlsParameters)
        };
        var response = await CallAsync("ConnectPeerTransport", (client, options) => client.ConnectPeerTransportAsync(request, options));

        if (response.Status != ProtoSFUResultStatus.Success)
        {
            _logger.LogError("Error creating peer transport for user {UserId} in session {SessionId}. " +
                             "Error message: {message}", userId, sessionId, response.ErrorMessage);
            return SFUTransportConnectResult.SFUNodeError;
        }

        return SFUTransportConnectResult.NoError;
    }
    
    public async Task ClosePeerTransport(int sessionId, int userId)
    {
        var request = new RemoveTransportRequest() { SessionId = sessionId, UserId = userId };
        var response = await CallAsync("RemovePeerTransport", (client, options) => client.RemovePeerTransportAsync(request, options));
        
        if (response.Status != ProtoSFUResultStatus.Success)
        {
            throw new Exception($"Failed to remove peer transport: {response.ErrorMessage}");
        }
    }

    public async Task<(SFUOutboundCreateResult result, Outbound? outbound)> AddOutbound(int sessionId, int userId, 
        MediaSourceType sourceType, 
        MediaType mediaType,
        OutboundMediaSettings settings)
    {
        var request = new AddOutboundRequest
        {
            SessionId = sessionId,
            UserId = userId,
            MediaSourceType = (ProtoMediaSourceType)sourceType,
            MediaType = (ProtoMediaType)mediaType,
            Settings = new OutboundSettings
            {
                MimeType = ToMimeType(settings.VideoCodec),
                Nack = settings.IsNackEnabled,
                Rtx = settings.IsRtxEnabled,
                KeyFrameRequestDelayMs = (uint)Math.Max(0, settings.KeyFrameRequestDelayMs),
                Fec = settings.IsFecEnabled,
                Dtx = settings.IsDtxEnabled
            }
        };

        var response = await CallAsync("AddOutbound", (client, options) => client.AddOutboundAsync(request, options));
        
        if (response.Status != ProtoSFUResultStatus.Success)
        {
            _logger.LogError("Error creating outbound for user {UserId} in session {SessionId}. " +
                             "Error message: {message}", userId, sessionId, response.ErrorMessage);
            return (SFUOutboundCreateResult.SFUNodeError, null);
        }
        
        return (SFUOutboundCreateResult.NoError, new Outbound()
        {
            Id = response.ProducerId,
            MediaType = mediaType,
            SourceType = sourceType,
            SSRC = response.Ssrc,
            UserId = userId,
            MimeType = response.Codec?.MimeType ?? string.Empty,
            PayloadType = (int)(response.Codec?.PayloadType ?? 0),
            ClockRate = (int)(response.Codec?.ClockRate ?? 0),
            Channels = (int)(response.Codec?.Channels ?? 1),
            RtxPayloadType = (int)response.RtxPayloadType,
            RtxSSRC = response.RtxSsrc,
            TransportCcExtensionId = (int)response.TransportCcExtensionId,
            IsFecEnabled = settings.IsFecEnabled,
            FecLossPercent = settings.FecLossPercent,
            IsDtxEnabled = settings.IsDtxEnabled
        });
    }

    // Empty leaves the SFU the usual codec of the source, which audio has anyway
    private static string ToMimeType(WebRTCVideoCodecs codec) => codec switch
    {
        WebRTCVideoCodecs.VP8 => "video/VP8",
        WebRTCVideoCodecs.H264 => "video/H264",
        WebRTCVideoCodecs.Undefined => string.Empty,
        _ => throw new NotSupportedException($"The SFU has no {codec} codec.")
    };

    public async Task<SFUOutboundCreateResult> UpdateOutbound(int sessionId, int userId, Outbound outbound)
    {
        CheckSFUServer();
        
        throw new NotImplementedException();
    }

    public async Task<SFUOutboundCreateResult> PauseOutbound(int sessionId, string outboundId)
    {
        return await SetOutboundPaused(sessionId, outboundId, true);
    }

    public async Task<SFUOutboundCreateResult> ResumeOutbound(int sessionId, string outboundId)
    {
        return await SetOutboundPaused(sessionId, outboundId, false);
    }

    private async Task<SFUOutboundCreateResult> SetOutboundPaused(int sessionId, string outboundId, bool paused)
    {
        var request = new OutboundActionRequest
        {
            SessionId = sessionId,
            ProducerId = outboundId
        };

        var response = paused
            ? await CallAsync("PauseOutbound", (client, options) => client.PauseOutboundAsync(request, options))
            : await CallAsync("ResumeOutbound", (client, options) => client.ResumeOutboundAsync(request, options));

        if (response.Status != ProtoSFUResultStatus.Success)
        {
            _logger.LogError("Error {action} outbound {OutboundId} in session {SessionId}. Error message: {message}",
                paused ? "pausing" : "resuming", outboundId, sessionId, response.ErrorMessage);
            return SFUOutboundCreateResult.SFUNodeError;
        }

        return SFUOutboundCreateResult.NoError;
    }

    public async Task<SFUOutboundCreateResult> RemoveOutbound(int sessionId, int userId, string outboundId)
    {
        var request = new RemoveOutboundRequest()
        {
            SessionId = sessionId,
            ProducerId = outboundId,
        };
        var response = await CallAsync("RemoveOutbound", (client, options) => client.RemoveOutboundAsync(request, options));
        
        return response.Status == ProtoSFUResultStatus.Success 
            ? SFUOutboundCreateResult.NoError 
            : SFUOutboundCreateResult.SFUNodeError;
    }

    public async Task<SFUOutboundDowngradeResult> DowngradeOutbound(int sessionId, int userId, MediaSourceType sourceType, MediaType? type = null)
    {
        _logger.LogWarning("DowngradeOutbound is not supported in the gRPC service");
        return SFUOutboundDowngradeResult.SFUNodeError;
    }

    public async Task<SFUOutboundUpgradeResult> UpgradeOutbound(int sessionId, int userId, MediaSourceType sourceType, MediaType? type = null)
    {
        _logger.LogWarning("UpgradeOutbound is not supported in the gRPC service");
        return SFUOutboundUpgradeResult.SFUNodeError;
    }

    public async Task<(SFUInboundCreateResult result, Inbound? inbound)> AddInbound(int sessionId, int userId,
        Outbound outbound, InboundMediaSettings settings)
    {
        var request = new AddInboundRequest()
        {
            SessionId = sessionId,
            UserId = userId,
            ProducerId = outbound.Id,
            Settings = new InboundSettings { Nack = settings.IsNackEnabled }
        };

        var response = await CallAsync("AddInbound", (client, options) => client.AddInboundAsync(request, options));
        
        if (response.Status != ProtoSFUResultStatus.Success)
        {
            _logger.LogError("Error creating inbound of outbound {OutboundId} for user {UserId} in session {SessionId}. " +
                             "Error message: {message}", outbound.Id, userId, sessionId, response.ErrorMessage);
            return (SFUInboundCreateResult.SFUNodeError, null);
        }

        return (SFUInboundCreateResult.NoError, new Inbound()
        {
            Id = response.ConsumerId,
            UserId = outbound.UserId,
            SSRC = response.Ssrc,
            SourceType = outbound.SourceType,
            MediaType = outbound.MediaType,
            MimeType = response.Codec?.MimeType ?? string.Empty,
            PayloadType = (int)(response.Codec?.PayloadType ?? 0),
            ClockRate = (int)(response.Codec?.ClockRate ?? 0),
            Channels = (int)(response.Codec?.Channels ?? 1),
            IsNackEnabled = settings.IsNackEnabled,
            JitterBufferMs = settings.JitterBufferMs
        });
    }

    public async Task<SFUInboundCreateResult> RemoveInbound(int sessionId, int userId, string inboundId)
    {
        var request = new RemoveInboundRequest()
        {
            SessionId = sessionId,
            ConsumerId = inboundId,
        };
        var response = await CallAsync("RemoveInbound", (client, options) => client.RemoveInboundAsync(request, options));
        
        return response.Status == ProtoSFUResultStatus.Success 
            ? SFUInboundCreateResult.NoError 
            : SFUInboundCreateResult.SFUNodeError;
    }

    public async Task<SFUInboundCreateResult> PauseInbound(int sessionId, string inboundId)
    {
        return await SetInboundPaused(sessionId, inboundId, true);
    }

    public async Task<SFUInboundCreateResult> ResumeInbound(int sessionId, string inboundId)
    {
        return await SetInboundPaused(sessionId, inboundId, false);
    }

    private async Task<SFUInboundCreateResult> SetInboundPaused(int sessionId, string inboundId, bool paused)
    {
        var request = new InboundActionRequest
        {
            SessionId = sessionId,
            ConsumerId = inboundId
        };

        var response = paused
            ? await CallAsync("PauseInbound", (client, options) => client.PauseInboundAsync(request, options))
            : await CallAsync("ResumeInbound", (client, options) => client.ResumeInboundAsync(request, options));

        if (response.Status != ProtoSFUResultStatus.Success)
        {
            _logger.LogError("Error {action} inbound {InboundId} in session {SessionId}. Error message: {message}",
                paused ? "pausing" : "resuming", inboundId, sessionId, response.ErrorMessage);
            return SFUInboundCreateResult.SFUNodeError;
        }

        return SFUInboundCreateResult.NoError;
    }

    public async Task<List<Inbound>> GetAllInbounds(int sessionId, int? userId = null)
    {
        _logger.LogWarning("GetAllInbounds is not supported in the gRPC service");
        return new List<Inbound>();
    }
    
    public async Task LoadSession(int sessionId)
    {
        var request = new SessionRequest
        {
            SessionId = sessionId
        };
        var response = await CallAsync("LoadSession", (client, options) => client.LoadSessionAsync(request, options));
        
        if (response.Status != ProtoSFUResultStatus.Success)
        {
            throw new Exception($"Failed to load SFU session: {response.ErrorMessage}");
        }
    }

    public async Task RestartSession(int sessionId)
    {
        CheckSFUServer();
        
        throw new NotImplementedException();
    }

    public async Task UnloadSession(int sessionId)
    {
        var request = new SessionRequest
        {
            SessionId = sessionId
        };
        var response = await CallAsync("UnloadSession", (client, options) => client.UnloadSessionAsync(request, options));
        
        if (response.Status != ProtoSFUResultStatus.Success)
        {
            throw new Exception($"Failed to unload SFU session: {response.ErrorMessage}");
        }
    }

    /// <summary>
    /// Runs one RPC with a deadline, so that a hung SFU node cannot block its callers forever.
    /// </summary>
    /// <exception cref="TimeoutException">The node did not answer in time.</exception>
    private async Task<TResponse> CallAsync<TResponse>(string operation,
        Func<MediasoupServer.MediasoupServerClient, CallOptions, AsyncUnaryCall<TResponse>> call)
    {
        CheckSFUServer();

        var timeout = TimeSpan.FromSeconds(_connectionOptions.CallTimeoutSeconds);
        var options = new CallOptions(deadline: DateTime.UtcNow.Add(timeout),
            cancellationToken: _cts?.Token ?? CancellationToken.None);
        try
        {
            return await call(_grpcClient!, options);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.DeadlineExceeded)
        {
            _logger.LogWarning("SFU call {Operation} on node {NodeId} timed out after {Timeout}.", operation, NodeId, timeout);
            throw new TimeoutException($"SFU call {operation} timed out after {timeout}.", e);
        }
    }

    private void CheckSFUServer()
    {
        if (!IsConnected)
            throw new InvalidOperationException($"SFU server is not connected");

        if (_grpcClient == null)
            throw new InvalidOperationException("gRPC client is not initialized. Connection channel might be closed");
        
        
        if (_activeChannel != null)
        {
            var state = _activeChannel.State;
            
            if (state == ConnectivityState.Shutdown)
            {
                throw new InvalidOperationException("The gRPC channel to the SFU server has been permanently shut down");
            }
        }
    }
}
