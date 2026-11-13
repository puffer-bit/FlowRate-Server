import assert from 'node:assert/strict';
import test from 'node:test';
import { MediaSourceType } from '../../core/enums/media-source-type.js';
import { MediaType } from '../../core/enums/media-type.js';
import { getMediaProfile, getRtxPayloadType, ROUTER_CODECS } from './media-profiles.js';

test('AV1 producers match the router and retain RTX and transport feedback', () => {
    for (const source of [MediaSourceType.Screen, MediaSourceType.Camera]) {
        const profile = getMediaProfile(source, MediaType.Video, { mimeType: 'video/av1' });
        const routerCodec = ROUTER_CODECS.find(codec => codec.mimeType === profile.codec.mimeType);
        assert.ok(routerCodec);
        assert.equal(profile.codec.mimeType, 'video/AV1');
        assert.equal(profile.codec.payloadType, routerCodec.preferredPayloadType);
        assert.equal(profile.codec.clockRate, routerCodec.clockRate);
        assert.equal(profile.rtx, true);
        assert.ok(profile.codec.rtcpFeedback?.some(feedback => feedback.type === 'transport-cc'));
        assert.equal(getRtxPayloadType({ codecs: [{
            kind: 'video', mimeType: 'video/rtx', clockRate: 90000,
            preferredPayloadType: 105, parameters: { apt: profile.codec.payloadType }
        }] }, profile.codec), 105);
    }
});

test('AV1 without retransmissions keeps keyframe feedback', () => {
    const profile = getMediaProfile(MediaSourceType.Screen, MediaType.Video, { mimeType: 'video/AV1', rtx: false });
    assert.equal(profile.rtx, false);
    assert.ok(!profile.codec.rtcpFeedback?.some(feedback => feedback.type === 'nack' && !feedback.parameter));
    assert.ok(profile.codec.rtcpFeedback?.some(feedback => feedback.parameter === 'pli'));
});

test('existing defaults and unsupported codec rejection are preserved', () => {
    assert.equal(getMediaProfile(MediaSourceType.Screen, MediaType.Video).codec.mimeType, 'video/H264');
    assert.equal(getMediaProfile(MediaSourceType.Camera, MediaType.Video).codec.mimeType, 'video/VP8');
    assert.throws(() => getMediaProfile(MediaSourceType.Screen, MediaType.Video, { mimeType: 'video/unknown' }));
    const payloads = ROUTER_CODECS.map(codec => codec.preferredPayloadType);
    assert.equal(new Set(payloads).size, payloads.length);
});