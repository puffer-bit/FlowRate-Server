using Shared.Enums.WebRTC;
using Shared.Models.Media;

namespace Server.Core.Models.HybridSession;

/// <summary>What the media settings of a session ask of a track we receive from a peer.</summary>
/// <param name="VideoCodec">The codec of a video track; Undefined for audio.</param>
/// <param name="IsNackEnabled">The SFU asks for lost video packets again, which the client resends on RTX.</param>
/// <param name="FecLossPercent">The loss the Opus FEC is prepared for.</param>
/// <param name="IsDtxEnabled">An Opus voice sends next to nothing while silent.</param>
public sealed record OutboundMediaSettings(
    WebRTCVideoCodecs VideoCodec,
    bool IsNackEnabled,
    bool IsRtxEnabled,
    int KeyFrameRequestDelayMs,
    bool IsFecEnabled,
    int FecLossPercent,
    bool IsDtxEnabled);

/// <summary>What the media settings of a session ask of a track a peer receives.</summary>
/// <param name="IsNackEnabled">The SFU resends lost packets when the receiver asks.</param>
/// <param name="JitterBufferMs">How long the receiver buffers the track.</param>
public sealed record InboundMediaSettings(bool IsNackEnabled, int JitterBufferMs);

public static class TrackMediaSettingsExtensions
{
    public static OutboundMediaSettings ForOutbound(this HybridMediaSettings media, MediaSourceType sourceType,
        MediaType mediaType) => new(
        mediaType != MediaType.Video ? WebRTCVideoCodecs.Undefined
            : sourceType == MediaSourceType.Camera ? media.CameraCodec
            : media.ScreenCodec,
        media.IsVideoNackEnabled,
        media.IsVideoRtxEnabled,
        media.KeyFrameRequestDelayMs,
        media.IsOpusFecEnabled,
        media.OpusPacketLossPercent,
        // A film or music in a screen share is not a pause, so only a voice goes quiet
        media.IsVoiceDtxEnabled && sourceType == MediaSourceType.Microphone);

    public static InboundMediaSettings ForInbound(this HybridMediaSettings media, MediaType mediaType) =>
        mediaType == MediaType.Audio
            ? new InboundMediaSettings(media.IsAudioNackEnabled, media.VoiceJitterBufferMs)
            : new InboundMediaSettings(media.IsVideoNackEnabled, media.VideoJitterBufferMs);
}
