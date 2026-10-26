import { MediaSourceType } from '../../core/enums/media-source-type.js';
import { MediaType } from '../../core/enums/media-type.js';
import { getClientRtpCapabilities, getMediaProfile, getRtxPayloadType, type OutboundSettings } from './media-profiles.js';
import type {
    Consumer,
    DtlsParameters,
    Producer,
    Router,
    RtpCapabilities,
    RtpCodecParameters,
    WebRtcServer,
    WebRtcTransport
} from "mediasoup/types";

interface Peer {
    transport: WebRtcTransport;
    /** Client DTLS context id; a new one means the client cannot reuse the old transport. */
    epoch: string;
    connected: boolean;
    connecting: Promise<void> | undefined;
}

interface ProducerInfo {
    producer: Producer;
    userId: number;
}

interface ConsumerInfo {
    consumer: Consumer;
    userId: number;
}

export interface OutboundResult {
    producerId: string;
    ssrc: number;
    codec: RtpCodecParameters | undefined;
    /** The stream the client retransmits lost packets on, when the track has one. */
    rtx: { payloadType: number; ssrc: number } | undefined;
}

export interface InboundResult {
    consumerId: string;
    ssrc: number;
    codec: RtpCodecParameters | undefined;
}

export class Session {
    private peers = new Map<number, Peer>();
    private pendingTransports = new Map<number, Promise<WebRtcTransport>>();
    private producers = new Map<string, ProducerInfo>();
    private consumers = new Map<string, ConsumerInfo>();

    private static readonly keyFrameRetryDelays = [1000, 2000, 4000];

    /** What a client receives with NACK, and without: then it asks only for key frames. */
    private readonly clientRtpCapabilities: RtpCapabilities;
    private readonly clientRtpCapabilitiesWithoutNack: RtpCapabilities;

    constructor(
        public readonly sessionId: number,
        public readonly portOffset: number,
        private readonly router: Router,
        private readonly server: WebRtcServer) {
        this.clientRtpCapabilities = getClientRtpCapabilities(router.rtpCapabilities);
        this.clientRtpCapabilitiesWithoutNack = getClientRtpCapabilities(router.rtpCapabilities, false);
        console.log(`[Mediasoup Session ${this.sessionId}] Session started`);
    }

    public async createPeerTransport(userId: number, clientEpoch: string): Promise<WebRtcTransport> {
        const pending = this.pendingTransports.get(userId);
        if (pending) {
            return pending;
        }

        const existing = this.peers.get(userId);

        if (existing && this.isReusable(existing.transport) && existing.epoch === clientEpoch) {
            console.log(`[Mediasoup Session ${this.sessionId}] User ${userId} rejoined, transport reused`);
            return existing.transport;
        }

        if (existing) {
            const reason = existing.epoch === clientEpoch
                ? `transport is unusable (DTLS ${existing.transport.dtlsState})`
                : 'rebuilt its DTLS context';
            console.log(`[Mediasoup Session ${this.sessionId}] User ${userId} ${reason}, transport recreated`);
            this.closePeerTransport(userId);
        }

        const creation = this.router.createWebRtcTransport({
            webRtcServer: this.server,
            enableUdp: true,
            enableTcp: true,
            preferUdp: true
        });

        this.pendingTransports.set(userId, creation);

        try {
            const transport = await creation;
            this.peers.set(userId, { transport, epoch: clientEpoch, connected: false, connecting: undefined });
            this.watchTransport(userId, transport);

            console.log(`[Mediasoup Session ${this.sessionId}] User ${userId} transport created`);
            return transport;
        } finally {
            this.pendingTransports.delete(userId);
        }
    }

    public async connectPeerTransport(userId: number, clientDtls: DtlsParameters): Promise<void> {
        const peer = this.peers.get(userId);

        if (!peer || peer.transport.closed) {
            throw new Error(`No transport for user ${userId}`);
        }

        if (peer.connected) {
            return;
        }

        if (peer.connecting) {
            return peer.connecting;
        }

        if (!clientDtls?.fingerprints?.length) {
            throw new Error(`No fingerprints from user ${userId}`);
        }

        const role = clientDtls.role ?? 'client';

        peer.connecting = peer.transport
            .connect({
                dtlsParameters: {
                    role,
                    fingerprints: clientDtls.fingerprints.map(({ algorithm, value }) => ({ algorithm, value }))
                }
            })
            .then(() => {
                peer.connected = true;
                console.log(`[Mediasoup Session ${this.sessionId}] User ${userId} connected as DTLS ${role}`);
            })
            .finally(() => peer.connecting = undefined);

        return peer.connecting;
    }

    public closePeerTransport(userId: number): void {
        const peer = this.peers.get(userId);

        if (peer && !peer.transport.closed) {
            peer.transport.close();
        }

        this.peers.delete(userId);

        console.log(`[Mediasoup Session ${this.sessionId}] User ${userId} transport closed`);
    }

    /**
     * Key frame requests that reach a video producer within the delay of the settings of the
     * previous one are merged into a single request at its end. On a lossy network every receiver
     * asks for a key frame on every loss, and the burst of large key frames it would cause adds to
     * the congestion.
     */
    public async addOutbound(userId: number, mediaSourceType: MediaSourceType, mediaType: MediaType,
        settings?: OutboundSettings): Promise<OutboundResult> {
        const transport = this.getTransport(userId, 'outbound');
        const profile = getMediaProfile(mediaSourceType, mediaType, settings);
        const { kind, codec } = profile;
        const ssrc = Session.randomSsrc();

        const rtxPayloadType = profile.rtx ? getRtxPayloadType(this.router.rtpCapabilities, codec) : undefined;
        const rtx = rtxPayloadType === undefined ? undefined : { payloadType: rtxPayloadType, ssrc: Session.randomSsrc(ssrc) };

        const producer = await transport.produce({
            kind,
            rtpParameters: {
                codecs: rtx
                    ? [codec, { mimeType: `${kind}/rtx`, payloadType: rtx.payloadType, clockRate: codec.clockRate, parameters: { apt: codec.payloadType } }]
                    : [codec],
                encodings: [rtx ? { ssrc, rtx: { ssrc: rtx.ssrc } } : { ssrc }]
            },
            keyFrameRequestDelay: profile.keyFrameRequestDelay
        });

        this.producers.set(producer.id, { producer, userId });
        producer.on('transportclose', () => this.producers.delete(producer.id));

        console.log(`[Mediasoup Session ${this.sessionId}] Outbound ${producer.id} for user ${userId} created`);
        return {
            producerId: producer.id,
            ssrc,
            codec: producer.rtpParameters.codecs[0],
            rtx
        };
    }

    public async setOutboundPaused(producerId: string, paused: boolean): Promise<void> {
        const info = this.producers.get(producerId);
        if (!info) {
            throw new Error(`Outbound ${producerId} not found`);
        }

        if (paused) {
            await info.producer.pause();
        } else {
            await info.producer.resume();
        }

        console.log(`[Mediasoup Session ${this.sessionId}] Outbound ${producerId} of user ${info.userId} ${paused ? 'paused' : 'resumed'}`);
    }

    public removeOutbound(producerId: string): void {
        const info = this.producers.get(producerId);
        if (!info) {
            return;
        }

        info.producer.close();
        this.producers.delete(producerId);
        console.log(`[Mediasoup Session ${this.sessionId}] Outbound ${producerId} for user ${info.userId} removed`);
    }

    /**
     * @param nack Whether the consumer keeps what it sends and resends it, on the same SSRC, when
     * the client asks. Without it the client only asks for key frames. Left out, video has it and
     * audio does not, as before there were settings.
     */
    public async addInbound(userId: number, producerId: string, nack?: boolean): Promise<InboundResult> {
        const transport = this.getTransport(userId, 'inbound');
        const kind = this.producers.get(producerId)?.producer.kind;
        const enableRtx = nack ?? kind === 'video';

        const consumer = await transport.consume({
            producerId,
            rtpCapabilities: enableRtx ? this.clientRtpCapabilities : this.clientRtpCapabilitiesWithoutNack,
            enableRtx,
            paused: true
        });

        this.consumers.set(consumer.id, { consumer, userId });
        consumer.on('transportclose', () => this.consumers.delete(consumer.id));
        consumer.on('producerclose', () => this.consumers.delete(consumer.id));

        console.log(`[Mediasoup Session ${this.sessionId}] Inbound ${consumer.id} of outbound ${producerId} for user ${userId} created`);
        return {
            consumerId: consumer.id,
            ssrc: consumer.rtpParameters.encodings?.[0]?.ssrc ?? 0,
            codec: consumer.rtpParameters.codecs[0]
        };
    }

    public removeInbound(consumerId: string): void {
        const info = this.consumers.get(consumerId);
        if (info) {
            info.consumer.close();
            this.consumers.delete(consumerId);
            console.log(`[Mediasoup Session ${this.sessionId}] Inbound ${consumerId} for user ${info.userId} removed`);
        }
    }

    public async setInboundPaused(consumerId: string, paused: boolean): Promise<void> {
        const info = this.consumers.get(consumerId);
        if (!info) {
            throw new Error(`Inbound ${consumerId} not found`);
        }

        if (paused) {
            await info.consumer.pause();
        } else {
            if (info.consumer.kind === 'video') {
                const sentBefore = await this.getSentBytes(info.consumer);
                await info.consumer.resume();
                void this.requestKeyFrameUntilSent(info.consumer, sentBefore);
            } else {
                await info.consumer.resume();
            }
        }

        console.log(`[Mediasoup Session ${this.sessionId}] Inbound ${consumerId} of user ${info.userId} ${paused ? 'paused' : 'resumed'}`);
    }

    public close(): void {
        this.router.close();
        this.peers.clear();
        this.pendingTransports.clear();
        this.producers.clear();
        this.consumers.clear();
        console.log(`[Mediasoup Session ${this.sessionId}] Session closed`);
    }

    /** A random SSRC, never 0 and never the one given. */
    private static randomSsrc(other?: number): number {
        let ssrc: number;
        do {
            ssrc = Math.floor(Math.random() * 4294967295) + 1;
        } while (ssrc === other);
        return ssrc;
    }

    /** A transport whose DTLS failed or closed never carries media again, even for the same client context. */
    private isReusable(transport: WebRtcTransport): boolean {
        return !transport.closed && transport.dtlsState !== 'failed' && transport.dtlsState !== 'closed';
    }

    private watchTransport(userId: number, transport: WebRtcTransport): void {
        const prefix = `[Mediasoup Session ${this.sessionId}] User ${userId}`;

        transport.on('icestatechange', state => {
            if (state === 'disconnected') {
                console.warn(`${prefix} ICE disconnected, waiting for the client to come back`);
            } else if (state === 'connected' || state === 'completed') {
                console.log(`${prefix} ICE ${state}`);
            }
        });

        transport.on('iceselectedtuplechange', tuple => {
            console.log(`${prefix} now reached at ${tuple.remoteIp}:${tuple.remotePort} over ${tuple.protocol}`);
        });

        transport.on('dtlsstatechange', state => {
            if (state === 'failed' || state === 'closed') {
                console.warn(`${prefix} DTLS ${state}, the transport is recreated on the next join`);
                const peer = this.peers.get(userId);
                if (peer?.transport === transport) {
                    peer.connected = false;
                }
            }
        });
    }

    private getTransport(userId: number, purpose: 'outbound' | 'inbound'): WebRtcTransport {
        const peer = this.peers.get(userId);
        if (!peer) {
            console.error(`[Mediasoup Session ${this.sessionId}] Failed to create ${purpose}. User ${userId} not connected.`);
            throw new Error(`User not connected`);
        }
        return peer.transport;
    }

    private async getSentBytes(consumer: Consumer): Promise<number> {
        const stats = await consumer.getStats();
        return stats.find(item => item.type === 'outbound-rtp')?.byteCount ?? 0;
    }

    private async requestKeyFrameUntilSent(consumer: Consumer, sentBefore: number): Promise<void> {
        try {
            for (const delay of Session.keyFrameRetryDelays) {
                await new Promise(resolve => setTimeout(resolve, delay));

                if (consumer.closed || consumer.paused || consumer.producerPaused) {
                    return;
                }

                if (await this.getSentBytes(consumer) > sentBefore) {
                    return;
                }

                await consumer.requestKeyFrame();
                console.log(`[Mediasoup Session ${this.sessionId}] Inbound ${consumer.id} sent nothing in ${delay} ms, key frame requested again`);
            }
        } catch (err: any) {
            if (!consumer.closed) {
                console.error(`[Mediasoup Session ${this.sessionId}] Failed to request a key frame for inbound ${consumer.id}: ${err.message}`);
            }
        }
    }
}
