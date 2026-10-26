import { SFUServer } from './infrastructure/server.js'
import { loadConfig } from './infrastructure/config.js'

let server: SFUServer | undefined;

async function bootstrap() {
    try {
        const config = loadConfig();
        server = new SFUServer(config);

        console.log('[Mediasoup Node] Starting server...');
        await server.start();
    } catch (error) {
        console.error('[Mediasoup Node] Failed to start server', error);
        process.exit(1);
    }
}

const shutdown = async () => {
    await server?.stop();
    process.exit(0);
};

process.on('SIGINT', shutdown);
process.on('SIGTERM', shutdown);

bootstrap();
