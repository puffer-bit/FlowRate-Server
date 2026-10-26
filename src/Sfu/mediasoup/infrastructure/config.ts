import 'dotenv/config';
import { isIP } from 'net';
import { availableParallelism } from 'os';
import type { WorkerLogLevel } from 'mediasoup/types';

export interface RtcEndpoint {
    listenIp: string;
    announcedIp: string;
    port: number;
}

export interface SfuConfig {
    /** Address the gRPC API listens on, e.g. 0.0.0.0:50051. Only the server should reach it. */
    grpcBind: string;
    rtc: {
        endpoints: [RtcEndpoint, ...RtcEndpoint[]];
    };
    logLevel: WorkerLogLevel;
    workers: number;
}

const LOG_LEVELS: WorkerLogLevel[] = ['debug', 'warn', 'error', 'none'];

function readPort(name: string, fallback: number): number {
    const raw = process.env[name];
    if (raw === undefined || raw === '') return fallback;

    const port = Number(raw);
    if (!Number.isInteger(port) || port < 1 || port > 65535)
        throw new Error(`${name} must be a port number (1-65535), got '${raw}'`);
    return port;
}

function parsePort(name: string, raw: string): number {
    const port = Number(raw);
    if (!/^\d+$/.test(raw) || port < 1 || port > 65535)
        throw new Error(`${name} must be a port number (1-65535), got '${raw}'`);
    return port;
}

function isWildcardIp(ip: string): boolean {
    return ip === '0.0.0.0' || ip === '::';
}

function parseEndpoint(name: string, entry: string, defaultListenIp: string, defaultPort: number): RtcEndpoint {
    let rest = entry;
    let listenIp = defaultListenIp;

    const at = rest.lastIndexOf('@');
    if (at >= 0) {
        listenIp = rest.slice(at + 1).trim();
        rest = rest.slice(0, at).trim();
        if (isIP(listenIp) === 0)
            throw new Error(`${name}: listen address in '${entry}' must be an IP address, got '${listenIp}'`);
    }

    let announcedIp = rest;
    let port = defaultPort;

    const bracketed = /^\[(.+)\](?::(.*))?$/.exec(rest);
    if (bracketed) {
        const [, ip = '', portText] = bracketed;
        announcedIp = ip;
        if (portText !== undefined)
            port = parsePort(`${name} ('${entry}')`, portText);
    } else if (rest.split(':').length === 2) {
        const colon = rest.indexOf(':');
        announcedIp = rest.slice(0, colon);
        port = parsePort(`${name} ('${entry}')`, rest.slice(colon + 1));
    }

    if (isIP(announcedIp) === 0)
        throw new Error(`${name}: '${entry}' must be an IP address, optionally with :port and @listen-ip`);
    if (isWildcardIp(announcedIp))
        throw new Error(`${name}: '${entry}' cannot announce a wildcard address`);
    if (isIP(listenIp) !== isIP(announcedIp))
        throw new Error(`${name}: '${entry}' mixes IPv4 and IPv6 between the announced and the listen address`);

    return { listenIp, announcedIp, port };
}

function bindingsConflict(a: RtcEndpoint, b: RtcEndpoint, workers: number): boolean {
    if (Math.abs(a.port - b.port) >= workers || isIP(a.listenIp) !== isIP(b.listenIp))
        return false;
    return a.listenIp === b.listenIp || isWildcardIp(a.listenIp) || isWildcardIp(b.listenIp);
}

function readEndpoints(listenIp: string, defaultPort: number, workers: number): [RtcEndpoint, ...RtcEndpoint[]] {
    const listName = 'RTC_ANNOUNCED_IPS';
    const legacyName = 'RTC_ANNOUNCED_IP';

    let name = listName;
    let raw = process.env[listName];
    if (!raw?.trim()) {
        name = legacyName;
        raw = process.env[legacyName];
    }

    const entries = (raw ?? '').split(',').map(e => e.trim()).filter(e => e.length > 0);
    if (entries.length === 0) {
        if (isWildcardIp(listenIp))
            throw new Error(`${listName} is required when RTC_LISTEN_IP is a wildcard address`);
        return [{ listenIp, announcedIp: listenIp, port: defaultPort }];
    }

    const [first, ...others] = entries.map(entry => parseEndpoint(name, entry, listenIp, defaultPort));
    if (!first)
        throw new Error(`${listName} is empty`);
    const endpoints: [RtcEndpoint, ...RtcEndpoint[]] = [first, ...others];

    for (const e of endpoints) {
        if (e.port + workers - 1 > 65535)
            throw new Error(`${name}: ports ${e.port}..${e.port + workers - 1} do not fit for ${workers} workers`);
    }

    for (const [i, b] of endpoints.entries()) {
        for (const a of endpoints.slice(0, i)) {
            if (a.announcedIp === b.announcedIp && a.port === b.port)
                throw new Error(`${name}: ${b.announcedIp}:${b.port} is listed twice`);
            if (bindingsConflict(a, b, workers))
                throw new Error(`${name}: ${a.announcedIp} and ${b.announcedIp} would both bind ` +
                    `${b.listenIp}:${b.port}; give them different ports or listen addresses ` +
                    `(each worker takes the next port, MEDIASOUP_WORKERS=${workers})`);
        }
    }

    return endpoints;
}

function readIp(name: string, fallback: string | undefined): string {
    const value = process.env[name] || fallback;
    if (!value)
        throw new Error(`${name} is required`);
    if (isIP(value) === 0)
        throw new Error(`${name} must be an IP address, got '${value}'`);
    return value;
}

function readWorkers(): number {
    const raw = process.env.MEDIASOUP_WORKERS;
    if (raw === undefined || raw === '') return 1;
    if (raw === 'auto') return availableParallelism();

    const count = Number(raw);
    if (!Number.isInteger(count) || count < 1 || count > 256)
        throw new Error(`MEDIASOUP_WORKERS must be 'auto' or a number (1-256), got '${raw}'`);
    return count;
}

export function loadConfig(): SfuConfig {
    const grpcBind = process.env.GRPC_BIND || '127.0.0.1:50051';
    if (!/^.+:\d+$/.test(grpcBind))
        throw new Error(`GRPC_BIND must look like host:port, got '${grpcBind}'`);

    const listenIp = readIp('RTC_LISTEN_IP', '127.0.0.1');
    const workers = readWorkers();
    const endpoints = readEndpoints(listenIp, readPort('RTC_PORT', 4433), workers);

    const logLevel = (process.env.MEDIASOUP_LOG_LEVEL || 'error') as WorkerLogLevel;
    if (!LOG_LEVELS.includes(logLevel))
        throw new Error(`MEDIASOUP_LOG_LEVEL must be one of ${LOG_LEVELS.join(', ')}, got '${logLevel}'`);

    return {
        grpcBind,
        rtc: { endpoints },
        logLevel,
        workers
    };
}
