import assert from 'node:assert/strict';
import { createServer } from 'node:net';
import test from 'node:test';
import * as grpc from '@grpc/grpc-js';
import { mediaProto } from '../api/gRPC/proto-loader.js';
import { readAuth } from './auth.js';
import { SFUServer } from './server.js';

async function freePort(): Promise<number> {
    const server = createServer();
    await new Promise<void>((resolve, reject) => server.once('error', reject).listen(0, '127.0.0.1', resolve));
    const address = server.address();
    if (!address || typeof address === 'string') throw new Error('No port');
    await new Promise<void>((resolve, reject) => server.close(err => err ? reject(err) : resolve()));
    return address.port;
}

function call(client: any, method: string, request: object, authorization?: string): Promise<any> {
    const metadata = new grpc.Metadata();
    if (authorization !== undefined) metadata.set('authorization', authorization);
    return new Promise((resolve, reject) => client[method](request, metadata, (err: grpc.ServiceError | null, response: unknown) =>
        err ? reject(err) : resolve(response)));
}

test('Basic authentication is required on Ping and session operations', { timeout: 15000 }, async () => {
    const grpcPort = await freePort();
    const rtcPort = await freePort();
    const sfu = new SFUServer({
        grpcBind: `127.0.0.1:${grpcPort}`, auth: { username: 'user', password: 'sëcret:123' },
        rtc: { endpoints: [{ listenIp: '127.0.0.1', announcedIp: '127.0.0.1', port: rtcPort }] },
        logLevel: 'error', workers: 1,
    });
    await sfu.start();
    const client = new mediaProto.MediasoupServer(`127.0.0.1:${grpcPort}`, grpc.credentials.createInsecure());
    const valid = `Basic ${Buffer.from('user:sëcret:123', 'utf8').toString('base64')}`;
    const wrong = `Basic ${Buffer.from('user:wrong').toString('base64')}`;
    try {
        for (const header of [undefined, wrong, 'Bearer invalid']) {
            await assert.rejects(call(client, 'Ping', {}, header), (e: grpc.ServiceError) => e.code === grpc.status.UNAUTHENTICATED);
            await assert.rejects(call(client, 'LoadSession', { session_id: 10 }, header), (e: grpc.ServiceError) => e.code === grpc.status.UNAUTHENTICATED);
        }
        assert.equal((await call(client, 'Ping', {}, valid)).session_count, 0);
        await call(client, 'LoadSession', { session_id: 10 }, valid);
        assert.equal((await call(client, 'Ping', {}, valid)).session_count, 1);
    } finally {
        client.close();
        await sfu.stop();
    }
});

test('an SFU without configured credentials accepts anonymous calls', { timeout: 15000 }, async () => {
    const grpcPort = await freePort();
    const rtcPort = await freePort();
    const sfu = new SFUServer({
        grpcBind: `127.0.0.1:${grpcPort}`, auth: undefined,
        rtc: { endpoints: [{ listenIp: '127.0.0.1', announcedIp: '127.0.0.1', port: rtcPort }] },
        logLevel: 'error', workers: 1,
    });
    await sfu.start();
    const client = new mediaProto.MediasoupServer(`127.0.0.1:${grpcPort}`, grpc.credentials.createInsecure());
    try {
        assert.equal((await call(client, 'Ping', {})).session_count, 0);
    } finally {
        client.close();
        await sfu.stop();
    }
});

test('authentication configuration rejects partial credentials', () => {
    assert.equal(readAuth('', ''), undefined);
    assert.deepEqual(readAuth('user', 'password'), { username: 'user', password: 'password' });
    for (const [user, password] of [['user', ''], ['', 'password'], ['bad:name', 'password'], [' user ', 'password']] as const)
        assert.throws(() => readAuth(user, password));
});
