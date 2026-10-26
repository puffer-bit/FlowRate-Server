using Shared.Enums.WebRTC;

namespace Server.Core.Models.HybridSession;

/// <summary>
/// How the media of a hybrid session travel: codecs and the ways lost packets are made up for.
/// A change applies to the tracks started after it, see <see cref="TrackMediaSettingsExtensions"/>.
/// </summary>
public sealed record HybridMediaSettings(
    WebRTCVideoCodecs CameraCodec = WebRTCVideoCodecs.VP8,
    WebRTCVideoCodecs ScreenCodec = WebRTCVideoCodecs.H264,
    WebRTCAudioCodecs AudioCodec = WebRTCAudioCodecs.Opus,
    bool IsVideoNackEnabled = true,
    bool IsVideoRtxEnabled = true,
    int KeyFrameRequestDelayMs = 1000,
    bool IsAudioNackEnabled = false,
    bool IsOpusFecEnabled = true,
    int OpusPacketLossPercent = 10,
    bool IsVoiceDtxEnabled = true,
    int VoiceJitterBufferMs = 100,
    int VideoJitterBufferMs = 200)
{
    public static HybridMediaSettings Default { get; } = new();
}
