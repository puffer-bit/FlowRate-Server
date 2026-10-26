import {Session} from "../session/session.js";
import {ROUTER_CODECS} from "../session/media-profiles.js";
import * as mediasoup from "mediasoup";
import type {WebRtcServer, Worker, WorkerLogLevel} from "mediasoup/types";
import type {RtcEndpoint} from "../config.js";

interface WorkerSlot {
    worker: Worker;
    server: WebRtcServer;
    portOffset: number;
    sessions: number;
}

export class SessionsManager {
    private slots: WorkerSlot[] = [];
    private sessions = new Map<number, Session>();
    private pendingSessions = new Map<number, Promise<Session>>();
    private stopping = false;

    public async start(endpoints: RtcEndpoint[], logLevel: WorkerLogLevel, workerCount: number): Promise<void> {
        console.log(`[Mediasoup Manager] Starting ${workerCount} worker(s)...`);

        for (let i = 0; i < workerCount; i++) {
            const worker = await mediasoup.createWorker({ logLevel });
            worker.on('died', (error) => {
                if (this.stopping) return;
                console.error(`[Mediasoup Manager] Worker ${worker.pid} died, exiting:`, error);
                setTimeout(() => process.exit(1), 1000);
            });

            const server = await worker.createWebRtcServer({
                listenInfos: endpoints.flatMap(({ listenIp, announcedIp, port }) => [
                    { protocol: 'udp' as const, ip: listenIp, announcedAddress: announcedIp, port: port + i },
                    { protocol: 'tcp' as const, ip: listenIp, announcedAddress: announcedIp, port: port + i }
                ])
            });

            this.slots.push({ worker, server, portOffset: i, sessions: 0 });

            const summary = endpoints
                .map(e => `${e.listenIp}:${e.port + i} (announced ${e.announcedIp})`)
                .join(', ');
            console.log(`[Mediasoup Manager] Worker ${worker.pid} started. WebRTC on ${summary}`);
        }
    }

    public async getOrCreateSession(sessionId: number): Promise<Session> {
        const existing = this.sessions.get(sessionId);
        if (existing) return existing;

        const pending = this.pendingSessions.get(sessionId);
        if (pending) return pending;

        const creation = this.createSession(sessionId);
        this.pendingSessions.set(sessionId, creation);
        try {
            return await creation;
        } finally {
            this.pendingSessions.delete(sessionId);
        }
    }

    private async createSession(sessionId: number): Promise<Session> {
        const slot = this.slots.reduce((best, s) => s.sessions < best.sessions ? s : best);
        const router = await slot.worker.createRouter({ mediaCodecs: ROUTER_CODECS });

        const session = new Session(sessionId, slot.portOffset, router, slot.server);

        slot.sessions++;
        router.observer.once('close', () => slot.sessions--);

        this.sessions.set(sessionId, session);
        console.log(`[Mediasoup Manager] New session ${sessionId} loaded on worker ${slot.worker.pid}`);
        return session;
    }

    public get sessionCount(): number {
        return this.sessions.size;
    }

    public get workerCount(): number {
        return this.slots.length;
    }

    public getSession(sessionId: number): Session | undefined {
        return this.sessions.get(sessionId);
    }

    public async deleteSession(sessionId: number): Promise<void> {
        const pending = this.pendingSessions.get(sessionId);
        if (pending) await pending.catch(() => undefined);

        const session = this.sessions.get(sessionId);
        if (session) {
            this.sessions.delete(sessionId);
            session.close();
            console.log(`[Mediasoup Manager] Session ${sessionId} unloaded`);
        }
    }

    public async stop(): Promise<void> {
        console.log('[Mediasoup Manager] Closing workers...');
        this.stopping = true;

        for (const session of this.sessions.values()) {
            try {
                session.close();
            } catch (err) {
                console.error(`[Mediasoup Manager] Failed to close session ${session.sessionId}:`, err);
            }
        }
        this.sessions.clear();

        for (const slot of this.slots) {
            slot.worker.close();
        }
        this.slots = [];
        console.log('[Mediasoup Manager] Workers closed');
    }
}
