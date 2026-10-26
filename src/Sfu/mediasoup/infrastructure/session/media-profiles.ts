import { MediaSourceType } from '../../core/enums/media-source-type.js';
import { MediaType } from '../../core/enums/media-type.js';
import type { MediaKind, RtcpFeedback, RtpCapabilities, RtpCodecCapability, RtpCodecParameters } from 'mediasoup/types';

const OPUS = { mimeType: 'audio/opus', clockRate: 48000, channels: 2, payloadType: 100 };
const VP8 = { mimeType: 'video/VP8', clockRate: 90000, payloadType: 96 };
const H264 = {
    mimeType: 'video/H264',
    clockRate: 90000,
    payloadType: 102,
    parameters: {
        'packetization-mode': 1,
        'profile-level-id': '42e01f',
        'level-asymmetry-allowed': 1
    }
};

const PLI_FIR_FEEDBACK: RtcpFeedback[] = [
    { type: 'nack', parameter: 'pli' },
    { type: 'ccm', parameter: 'fir' }
];

const NACK_FEEDBACK: RtcpFeedback = { type: 'nack', parameter: '' };

/** Codecs of every session router; each profile below must match one of them. */
export const ROUTER_CODECS: RtpCodecCapability[] = [
    { kind: 'audio', mimeType: OPUS.mimeType, clockRate: OPUS.clockRate, channels: OPUS.channels, preferredPayloadType: OPUS.payloadType },
    { kind: 'video', mimeType: VP8.mimeType, clockRate: VP8.clockRate, preferredPayloadType: VP8.payloadType },
    { kind: 'video', mimeType: H264.mimeType, clockRate: H264.clockRate, preferredPayloadType: H264.payloadType, parameters: H264.parameters }
];

/**
 * What a client can receive, used for every consumer instead of the router's own capabilities.
 * The client builds its SDP from the codec alone: it signals no RTX stream and no header
 * extensions. So retransmissions go out as the original packets on the primary SSRC, which the
 * client's jitter buffer accepts, rather than on an RTX SSRC it drops; and no extension bytes,
 * transport-cc sequence numbers or bandwidth probation padding are sent that nobody reads.
 */
export function getClientRtpCapabilities(routerCapabilities: RtpCapabilities, nack = true): RtpCapabilities {
    return {
        codecs: (routerCapabilities.codecs ?? [])
            .filter(codec => !/\/rtx$/i.test(codec.mimeType))
            .map(codec => ({
                ...codec,
                rtcpFeedback: (codec.rtcpFeedback ?? []).filter(fb =>
                    fb.type === 'ccm' || (fb.type === 'nack' && (nack || fb.parameter === 'pli')))
            })),
        headerExtensions: []
    };
}

/**
 * How a new track follows the media settings of its session. The coordinator sends them with
 * every track; a field it leaves out keeps the value used before there were settings.
 */
export interface OutboundSettings {
    /** The video codec, video/VP8 or video/H264; empty for the usual one of the source. */
    mimeType?: string;
    /** The SFU asks the client for lost video packets again. Needs {@link rtx}: the client resends only on RTX. */
    nack?: boolean;
    rtx?: boolean;
    keyFrameRequestDelayMs?: number;
    /** Opus in-band FEC. */
    fec?: boolean;
    /** Opus DTX of a voice; the sound of a screen never has it. */
    dtx?: boolean;
}

export const DEFAULT_OUTBOUND_SETTINGS: Required<OutboundSettings> = {
    mimeType: '',
    nack: true,
    rtx: true,
    keyFrameRequestDelayMs: 1000,
    fec: true,
    dtx: true
};

export interface MediaProfile {
    kind: MediaKind;
    codec: RtpCodecParameters;
    /**
     * Whether the client retransmits lost packets of this track on a separate RTX stream. The
     * producer then takes the RTX payload type of the router and its own RTX SSRC, both of which
     * the client is told.
     */
    rtx: boolean;
    /** Key frame requests within this many ms of the previous one are merged; 0 for audio. */
    keyFrameRequestDelay: number;
}

const VIDEO_CODECS: Record<string, RtpCodecParameters> = {
    'video/vp8': VP8,
    'video/h264': H264
};

const DEFAULT_VIDEO_CODECS: Partial<Record<MediaSourceType, RtpCodecParameters>> = {
    [MediaSourceType.Camera]: VP8,
    [MediaSourceType.Screen]: H264
};

function videoCodec(source: MediaSourceType, mimeType: string): RtpCodecParameters {
    const codec = mimeType ? VIDEO_CODECS[mimeType.toLowerCase()] : DEFAULT_VIDEO_CODECS[source];
    if (!codec)
        throw new Error(`Video codec ${mimeType || 'of this source'} is not supported`);

    // A camera is motion, which VP8 is told; H.264 takes no such hint
    return source === MediaSourceType.Camera && codec === VP8
        ? { ...VP8, parameters: { 'content-hint': 'motion', 'sharpness': 3 } }
        : codec;
}

/** The payload type the router gives the RTX stream of a codec. */
export function getRtxPayloadType(routerCapabilities: RtpCapabilities, codec: RtpCodecParameters): number | undefined {
    return (routerCapabilities.codecs ?? []).find(capability =>
        /\/rtx$/i.test(capability.mimeType) && capability.parameters?.['apt'] === codec.payloadType)?.preferredPayloadType;
}

/** The codec a client sends for this source and media type, as the settings of the track ask. */
export function getMediaProfile(source: MediaSourceType, type: MediaType, settings: OutboundSettings = {}): MediaProfile {
    if (type === MediaType.Tunnel)
        throw new Error('Tunnel mode is not supported in this SFU version');

    const resolved = { ...DEFAULT_OUTBOUND_SETTINGS, ...settings };

    if (type === MediaType.Video && (source === MediaSourceType.Camera || source === MediaSourceType.Screen)) {
        // Without RTX the client does not resend, so a NACK of the SFU would go unanswered
        const repaired = resolved.nack && resolved.rtx;
        return {
            kind: 'video',
            codec: {
                ...videoCodec(source, resolved.mimeType),
                rtcpFeedback: repaired ? [NACK_FEEDBACK, ...PLI_FIR_FEEDBACK] : PLI_FIR_FEEDBACK
            },
            rtx: repaired,
            keyFrameRequestDelay: resolved.keyFrameRequestDelayMs
        };
    }

    if (type === MediaType.Audio && source === MediaSourceType.Microphone) {
        return {
            kind: 'audio',
            codec: { ...OPUS, parameters: { 'useinbandfec': resolved.fec ? 1 : 0, 'usedtx': resolved.dtx ? 1 : 0, 'minptime': 10, 'ptime': 20 } },
            rtx: false,
            keyFrameRequestDelay: 0
        };
    }

    if (type === MediaType.Audio && source === MediaSourceType.Screen) {
        return {
            kind: 'audio',
            codec: { ...OPUS, parameters: { 'useinbandfec': resolved.fec ? 1 : 0, 'usedtx': 0, 'stereo': 1, 'ptime': 20 } },
            rtx: false,
            keyFrameRequestDelay: 0
        };
    }

    throw new Error('Current media source type and media is not supported');
}
