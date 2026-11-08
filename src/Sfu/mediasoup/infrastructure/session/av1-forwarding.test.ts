import assert from 'node:assert/strict';
import test from 'node:test';
import { createWorker } from 'mediasoup';
import { MediaSourceType } from '../../core/enums/media-source-type.js';
import { MediaType } from '../../core/enums/media-type.js';
import { getClientRtpCapabilities, getMediaProfile, getProducerHeaderExtensions, ROUTER_CODECS } from './media-profiles.js';

test('the SFU recognizes and forwards the client single-layer AV1 keyframe', { timeout: 10000 }, async () => {
    const worker = await createWorker({ logLevel: 'error' });
    try {
        const router = await worker.createRouter({ mediaCodecs: ROUTER_CODECS });
        const sending = await router.createDirectTransport();
        const receiving = await router.createDirectTransport();
        const profile = getMediaProfile(MediaSourceType.Screen, MediaType.Video, { mimeType: 'video/AV1', rtx: false });
        const producer = await sending.produce({
            kind: 'video', rtpParameters: {
                codecs: [profile.codec], encodings: [{ ssrc: 1234, scalabilityMode: 'L1T1' }],
                headerExtensions: getProducerHeaderExtensions(profile.codec)
            }
        });
        const consumer = await receiving.consume({
            producerId: producer.id, rtpCapabilities: getClientRtpCapabilities(router.rtpCapabilities)
        });
        const forwarded = new Promise<Buffer>(resolve => consumer.once('rtp', resolve));
        // Produced by Av1RtpDependencyDescriptor, shared with Av1RtpDependencyDescriptorTests.
        producer.send(Buffer.from('90E8002A00000001000004D2BEDE000368C0000180003A4101000000081200', 'hex'));
        const packet = await forwarded;
        assert.deepEqual(packet.subarray(-3), Buffer.from('081200', 'hex'));
        assert.equal(consumer.rtpParameters.codecs[0]?.mimeType, 'video/AV1');
    } finally {
        worker.close();
    }
});