using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Shared.Enums.WebRTC;

namespace Server.Infrastructure.Database.Entities
{
    [Table("VideoSessions")]
    public class HybridSessionEntity : SessionEntity
    {
        public required WebRTCEngine EngineType { get; set; }
        
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool IsHostMustApprove { get; set; }

        public WebRTCVideoCodecs CameraCodec { get; set; } = WebRTCVideoCodecs.VP8;
        public WebRTCVideoCodecs ScreenCodec { get; set; } = WebRTCVideoCodecs.H264;
        public WebRTCAudioCodecs AudioCodec { get; set; } = WebRTCAudioCodecs.Opus;
        public bool IsVideoNackEnabled { get; set; } = true;
        public bool IsVideoRtxEnabled { get; set; } = true;
        public int KeyFrameRequestDelayMs { get; set; } = 1000;
        public bool IsAudioNackEnabled { get; set; }
        public bool IsOpusFecEnabled { get; set; } = true;
        public int OpusPacketLossPercent { get; set; } = 10;
        public bool IsVoiceDtxEnabled { get; set; } = true;
        public int VoiceJitterBufferMs { get; set; } = 100;
        public int VideoJitterBufferMs { get; set; } = 200;
        public bool IsActive { get; set; }
        public int ConnectedUsersCount { get; set; }
        
        [MaxLength(36)]
        public int HostId { get; set; }
        
        [MaxLength(36)]
        public string? HostPeerId { get; set; }
    }
}
