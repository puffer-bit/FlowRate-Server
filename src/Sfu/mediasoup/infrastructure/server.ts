import * as grpc from "@grpc/grpc-js";
import { SessionsManager } from "./manager/sessions-manager.js";
import { mediaProto } from "../api/gRPC/proto-loader.js";
import type { IceCandidate, RtpCodecParameters, WebRtcTransport } from "mediasoup/types";
import type { SfuConfig } from "./config.js";
import type { Session } from "./session/session.js";

class NotFoundError extends Error {}

export class SFUServer {
    private server: grpc.Server;
    private sessions: SessionsManager;

    constructor(private readonly config: SfuConfig) {
        this.server = new grpc.Server();
        this.sessions = new SessionsManager();
    }

    public async start(): Promise<void> {
        const { rtc, grpcBind, logLevel, workers } = this.config;
        await this.sessions.start(rtc.endpoints, logLevel, workers);

        this.server.addService(mediaProto.MediasoupServer.service, {
            Ping: this.handlePing.bind(this),

            LoadSession: this.handle(async ({ session_id }) => {
                await this.sessions.getOrCreateSession(session_id);
            }),
            UnloadSession: this.handle(({ session_id }) => this.sessions.deleteSession(session_id)),

            CreatePeerTransport: this.handle(async ({ session_id, user_id, client_epoch }) => {
                const session = this.requireSession(session_id);
                const transport = await session.createPeerTransport(user_id, client_epoch);
                return this.toTransportResponse(transport, session.portOffset);
            }),
            ConnectPeerTransport: this.handle(({ session_id, user_id, client_dtls }) =>
                this.requireSession(session_id).connectPeerTransport(user_id, client_dtls)),
            RemovePeerTransport: this.handle(({ session_id, user_id }) =>
                this.requireSession(session_id).closePeerTransport(user_id)),

            AddOutbound: this.handle(async ({ session_id, user_id, media_source_type, media_type, settings }) => {
                const result = await this.requireSession(session_id).addOutbound(user_id, media_source_type, media_type,
                    settings ? {
                        mimeType: settings.mime_type,
                        nack: settings.nack,
                        rtx: settings.rtx,
                        keyFrameRequestDelayMs: settings.key_frame_request_delay_ms,
                        fec: settings.fec,
                        dtx: settings.dtx
                    } : undefined);
                return {
                    producer_id: result.producerId,
                    ssrc: result.ssrc,
                    codec: this.toCodec(result.codec),
                    rtx_payload_type: result.rtx?.payloadType ?? 0,
                    rtx_ssrc: result.rtx?.ssrc ?? 0,
                    transport_cc_extension_id: result.transportCcExtensionId ?? 0
                };
            }),
            RemoveOutbound: this.handle(({ session_id, producer_id }) =>
                this.requireSession(session_id).removeOutbound(producer_id)),
            PauseOutbound: this.handle(({ session_id, producer_id }) =>
                this.requireSession(session_id).setOutboundPaused(producer_id, true)),
            ResumeOutbound: this.handle(({ session_id, producer_id }) =>
                this.requireSession(session_id).setOutboundPaused(producer_id, false)),

            AddInbound: this.handle(async ({ session_id, user_id, producer_id, settings }) => {
                const result = await this.requireSession(session_id).addInbound(user_id, producer_id, settings?.nack);
                return { consumer_id: result.consumerId, ssrc: result.ssrc, codec: this.toCodec(result.codec) };
            }),
            RemoveInbound: this.handle(({ session_id, consumer_id }) =>
                this.requireSession(session_id).removeInbound(consumer_id)),
            PauseInbound: this.handle(({ session_id, consumer_id }) =>
                this.requireSession(session_id).setInboundPaused(consumer_id, true)),
            ResumeInbound: this.handle(({ session_id, consumer_id }) =>
                this.requireSession(session_id).setInboundPaused(consumer_id, false)),
        });

        await new Promise<void>((resolve, reject) => {
            this.server.bindAsync(grpcBind, grpc.ServerCredentials.createInsecure(), (err) => {
                if (err) {
                    console.error(`[Mediasoup Node] Failed to start gRPC server: ${err.message}`);
                    return reject(err);
                }
                console.log(`[Mediasoup Node] gRPC server started at ${grpcBind}`);
                resolve();
            });
        });

        console.log('[Mediasoup Node] Server successfully started');
    }

    public async stop(): Promise<void> {
        console.log('[Mediasoup Node] Closing server...');

        this.server.forceShutdown();
        await this.sessions.stop();

        console.log('[Mediasoup Node] Server closed');
    }

    private handlePing(_call: grpc.ServerUnaryCall<unknown, object>, callback: grpc.sendUnaryData<object>): void {
        callback(null, {
            session_count: this.sessions.sessionCount,
            worker_count: this.sessions.workerCount,
            uptime_seconds: Math.floor(process.uptime())
        });
    }

    /**
     * Wraps a unary RPC whose response carries status and error_message:
     * the action's result is merged into a SUCCESS response, a thrown error becomes ERROR or NOT_FOUND.
     */
    private handle(action: (request: any) => Promise<object | void> | object | void): grpc.handleUnaryCall<any, object> {
        return async (call, callback) => {
            try {
                const result = await action(call.request);
                callback(null, { status: 'SUCCESS', ...result });
            } catch (err: any) {
                callback(null, {
                    status: err instanceof NotFoundError ? 'NOT_FOUND' : 'ERROR',
                    error_message: err.message
                });
            }
        };
    }

    private requireSession(sessionId: number): Session {
        const session = this.sessions.getSession(sessionId);
        if (!session) {
            throw new NotFoundError('Session not found');
        }
        return session;
    }

    private toTransportResponse(transport: WebRtcTransport, portOffset: number): object {
        const primary = this.config.rtc.endpoints[0];

        return {
            transport_id: transport.id,
            ice_parameters: {
                username_fragment: transport.iceParameters.usernameFragment,
                password: transport.iceParameters.password,
                server_ice_candidates: transport.iceCandidates.map((candidate, index) => ({
                    candidate: this.toSdpCandidate(candidate, index),
                    sdp_mid: '0',
                    sdp_m_line_index: 0
                })),
            },
            dtls_parameters: {
                role: transport.dtlsParameters.role,
                fingerprints: transport.dtlsParameters.fingerprints.map(({ algorithm, value }) => ({ algorithm, value }))
            },
            server_host_address: primary.announcedIp,
            server_port: primary.port + portOffset,
        };
    }

    private toCodec(codec: RtpCodecParameters | undefined): object {
        return {
            mime_type: codec?.mimeType ?? '',
            payload_type: codec?.payloadType ?? 0,
            clock_rate: codec?.clockRate ?? 0,
            channels: codec?.channels ?? 1
        };
    }

    private toSdpCandidate(candidate: IceCandidate, index: number): string {
        let line = `candidate:${candidate.foundation}${index} 1 ${candidate.protocol} ${candidate.priority} ` +
            `${candidate.address ?? candidate.ip} ${candidate.port} typ ${candidate.type}`;

        if (candidate.protocol === 'tcp' && candidate.tcpType) {
            line += ` tcptype ${candidate.tcpType}`;
        }

        return line;
    }
}
