import { createHash, timingSafeEqual } from 'node:crypto';
import * as grpc from '@grpc/grpc-js';

export interface SfuAuth {
    username: string;
    password: string;
}

/** Both credentials must be configured, or authentication remains disabled for existing installations. */
export function readAuth(username: string | undefined, password: string | undefined): SfuAuth | undefined {
    if (!username && !password) return undefined;
    if (!username || !password || username.includes(':') || username.trim() !== username)
        throw new Error('SFU_GRPC_USERNAME and SFU_GRPC_PASSWORD must both be set; the username cannot contain a colon or surrounding whitespace');
    return { username, password };
}

/** Apply to every RPC, including Ping. Compare fixed-length hashes without exposing the expected credentials. */
export function requireAuth<Request, Response>(auth: SfuAuth | undefined,
    handler: grpc.handleUnaryCall<Request, Response>): grpc.handleUnaryCall<Request, Response> {
    if (!auth) return handler;
    const expected = createHash('sha256').update(`Basic ${Buffer.from(`${auth.username}:${auth.password}`, 'utf8').toString('base64')}`).digest();
    return (call, callback) => {
        const values = call.metadata.get('authorization');
        const provided = values.length === 1 && typeof values[0] === 'string' ? values[0] : '';
        const actual = createHash('sha256').update(provided).digest();
        if (!timingSafeEqual(actual, expected)) {
            callback({ code: grpc.status.UNAUTHENTICATED, message: 'SFU authentication failed' }, null);
            return;
        }
        handler(call, callback);
    };
}
