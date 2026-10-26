using Server.Core.Models.HybridSession;
using Shared.Enums.WebRTC;
using Shared.Models.Media;

namespace Server.Core.Tests.Models;

public class TrackMediaSettingsTests
{
    private static readonly HybridMediaSettings Media = new(
        CameraCodec: WebRTCVideoCodecs.H264,
        ScreenCodec: WebRTCVideoCodecs.VP8,
        IsVideoNackEnabled: false,
        IsAudioNackEnabled: true,
        OpusPacketLossPercent: 25,
        VoiceJitterBufferMs: 150,
        VideoJitterBufferMs: 400);

    [Theory]
    [InlineData(MediaSourceType.Camera, MediaType.Video, WebRTCVideoCodecs.H264)]
    [InlineData(MediaSourceType.Screen, MediaType.Video, WebRTCVideoCodecs.VP8)]
    [InlineData(MediaSourceType.Screen, MediaType.Audio, WebRTCVideoCodecs.Undefined)]
    [InlineData(MediaSourceType.Microphone, MediaType.Audio, WebRTCVideoCodecs.Undefined)]
    public void Outbound_TakesTheCodecOfItsSource(MediaSourceType source, MediaType type, WebRTCVideoCodecs expected) =>
        Assert.Equal(expected, Media.ForOutbound(source, type).VideoCodec);

    [Fact]
    public void Outbound_OnlyAVoiceHasDtx()
    {
        Assert.True(Media.ForOutbound(MediaSourceType.Microphone, MediaType.Audio).IsDtxEnabled);
        Assert.False(Media.ForOutbound(MediaSourceType.Screen, MediaType.Audio).IsDtxEnabled);
    }

    [Fact]
    public void Outbound_CarriesTheLossRecovery()
    {
        var outbound = Media.ForOutbound(MediaSourceType.Screen, MediaType.Video);

        Assert.Equal((false, true, 1000, true, 25), (outbound.IsNackEnabled, outbound.IsRtxEnabled,
            outbound.KeyFrameRequestDelayMs, outbound.IsFecEnabled, outbound.FecLossPercent));
    }

    [Fact]
    public void Inbound_FollowsItsKind()
    {
        Assert.Equal(new InboundMediaSettings(true, 150), Media.ForInbound(MediaType.Audio));
        Assert.Equal(new InboundMediaSettings(false, 400), Media.ForInbound(MediaType.Video));
    }
}
