import * as protoLoader from '@grpc/proto-loader';
import * as grpc from '@grpc/grpc-js';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const PROTO_DIR = path.resolve(__dirname, '../../api/gRPC/contracts');
const PROTO_PATH = path.join(PROTO_DIR, 'mediasoup_service.proto');


const packageDefinition = protoLoader.loadSync(PROTO_PATH, {
    keepCase: true,
    longs: String,
    enums: Number,
    defaults: true,
    oneofs: true,
    includeDirs: [PROTO_DIR]
});

const grpcDescriptor = grpc.loadPackageDefinition(packageDefinition) as any;

export const mediaProto = grpcDescriptor.media;
