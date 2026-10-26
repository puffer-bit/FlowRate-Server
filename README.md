# FlowRate Server

The server side of FlowRate: text chats, voice and video sessions, push notifications and a browser control panel.
It runs as two containers:

- **server** (`src/Server/Server.Asp`, .NET 10): clients connect to it over WebSocket, it keeps the data in SQLite
  and serves the control panel.
- **mediasoup** (`src/Sfu/mediasoup`, Node.js): the SFU that carries voice and video over WebRTC; the server
  drives it through gRPC.

## Running it

On a Linux server (x64 or arm64), the latest release with its images from ghcr.io:

```sh
curl -fsSL https://github.com/puffer-bit/FlowRate-Server/releases/latest/download/install.sh | sudo sh
```

It asks for the public IP and the push notifications (a Firebase key or a relay, `--fcm-key FILE` /
`--push-relay URL` skip the question). Each release also ships an archive with the images for machines without
access to ghcr.io; see [`src/Scripts/package/README.txt`](src/Scripts/package/README.txt) for both.
Releases are published by the Release workflow (Actions tab); the images on ghcr.io have to be public once, in the
package settings, for the installer to pull them.

From the sources, with Docker:

```sh
git clone --recurse-submodules https://github.com/puffer-bit/FlowRate-Server.git
cd FlowRateServer
cp .env.example .env        # set PUBLIC_IP to the address clients reach this machine at
docker compose up -d --build
```

Ports:

| Port         | What                       |                        |
|--------------|----------------------------|------------------------|
| 26666/tcp    | clients                    | open from outside      |
| 4433/tcp+udp | voice and video (WebRTC)   | open from outside      |
| 7114/tcp     | control panel              | keep closed            |
| 50051/tcp    | server to SFU (gRPC)       | 127.0.0.1 only         |

The control panel has no authentication and listens on 127.0.0.1 only. Reach it through an SSH tunnel
(`ssh -L 7114:127.0.0.1:7114 user@server`, then https://localhost:7114/) and never expose it to the internet.

Push notifications and passkeys are off until `.env` turns them on; the comments there say how.

## Development

Requirements: .NET SDK 10, Node.js 24 or newer, Docker for the full stack.

```sh
src/Scripts/bash/build.sh     # the solution
src/Scripts/bash/test.sh      # every test project
src/Scripts/bash/run.sh       # the server; --panel builds the control panel first
cd src/Sfu/mediasoup && npm ci && npm run dev   # the SFU, configured by .env (see .env.example)
```

PowerShell versions of the scripts live in `src/Scripts/powershell`. The control panel has its own
[README](src/Web/admin-panel/README.md).

Layout:

- `src/Server` — the server, layered as Core → Application → Infrastructure → Asp, with a test project for each;
  `Server.PushRelay` is an optional relay that forwards pushes to FCM for servers without a Firebase key.
- `src/Sfu/mediasoup` — the SFU.
- `src/Web/admin-panel` — the control panel (React, TypeScript, Vite).
- `src/Shared` — the protocol shared with the client, a git submodule
  ([FlowRate-Shared](https://github.com/puffer-bit/FlowRate-Shared)).
- `docs` — [authentication](docs/authentication.md) and [roles](docs/roles.md).

## Secrets

Nothing secret belongs in the repository. `.env` and `secrets/` (the Firebase service account key) sit next to
`docker-compose.yml` and are git-ignored; the TLS certificate of the panel is generated on the first start and kept
in the data volume.

## License

[MIT](LICENSE)
