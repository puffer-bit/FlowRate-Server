> [!WARNING]
> FlowRate Server is still under development. Bugs, incomplete features, and breaking changes may occur.

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/AppIcons/RoundedCube/svg/wordmark-on-dark.svg">
    <source media="(prefers-color-scheme: light)" srcset="assets/AppIcons/RoundedCube/svg/wordmark-on-light.svg">
    <img src="assets/AppIcons/RoundedCube/svg/wordmark-on-light.svg" alt="FlowRate Server" width="720">
  </picture>
</p>

# FlowRate Server

The server side of FlowRate: text chats, voice and video sessions, push notifications and a browser control panel.
Deploy it on your own Linux server, then share the server address with your community. The installation includes the server, the media service for voice and screen sharing, and a browser control panel.

## Running it

On a Linux server (x64 or arm64), the newest pre-release with its images from ghcr.io:

```sh
curl -fsSL "$(curl -fsSL https://api.github.com/repos/puffer-bit/FlowRate-Server/releases \
    | grep -o 'https://[^"]*/releases/download/[^"]*/install.sh' | head -n 1)" | sudo sh
```

A particular one by its tag, from the [releases](https://github.com/puffer-bit/FlowRate-Server/releases):

```sh
curl -fsSL https://github.com/puffer-bit/FlowRate-Server/releases/download/<tag>/install.sh | sudo sh
```

It asks for the public IP and the push notifications (a Firebase key or a relay, `--fcm-key FILE` /
`--push-relay URL` skip the question). Each release also ships an archive with the images for machines without
access to ghcr.io; see [`src/Scripts/package/README.txt`](src/Scripts/package/README.txt) for both.
Releases are published by the Release workflow (Actions tab); the images on ghcr.io have to be public once, in the
package settings, for the installer to pull them.

From the sources, with Docker:

```sh
git clone --recurse-submodules https://github.com/puffer-bit/FlowRate-Server.git
cd FlowRate-Server
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

## SFU gRPC authentication

The SFU can require HTTP Basic credentials on every gRPC call. For the Compose-managed local SFU, set both
`SFU_GRPC_USERNAME` and `SFU_GRPC_PASSWORD` in `.env`, then recreate the SFU and server containers. Choose a
strong password; the username cannot contain `:` or surrounding whitespace. Leaving both empty keeps authentication
disabled for compatibility. Setting just one prevents the SFU from starting.

On a **new database**, Compose also seeds the local node with these credentials. Existing node settings are stored
in the database and are not overwritten by environment changes: set matching credentials under **SFU nodes →
Credentials → Edit** in the panel, or enter them when adding a node. Empty both fields using **Connect without
credentials** to remove stored credentials. Changing credentials reconnects the node and can interrupt media.
Each additional SFU can have its own environment variables and panel credentials.

Basic authentication does **not** encrypt credentials. The SFU itself serves plaintext gRPC, so use loopback or a
trusted private network; for untrusted networks put a TLS-terminating HTTP/2 gRPC proxy in front of the SFU and
select **Use TLS** in the panel. Keep the panel private too: it has no authentication. SFU credentials are stored
in the server database and supplied to the SFU as environment variables; protect the data volume, `.env` and
container configuration accordingly.

## SFU node selection and recovery

Each coordinator uses its assigned subset of the global SFU pool. New sessions are distributed by session ID
across the connected nodes; this is distribution by session count, not measured CPU or bandwidth load.
Healthy sessions keep their node when another node is added or recovers.

When a node loses its connection, sessions move to a connected alternative if one is assigned. Participants
stay in the session, but their media state is reset and clients must rejoin the SFU transport, so media can pause
during recovery. If all assigned nodes are unavailable, sessions wait for a connection to recover. Users may
still join and leave: they appear as `WaitingForServer`, and a successful join returns no transport until an
SFU becomes available. The client shows its existing waiting-for-SFU status and connects media automatically
when a node is ready. Host admission remains required for sessions that use it.

## Secrets

Nothing secret belongs in the repository. `.env` and `secrets/` (the Firebase service account key) sit next to
`docker-compose.yml` and are git-ignored; the TLS certificate of the panel is generated on the first start and kept
in the data volume.

For source builds, tests, and repository layout, see the [development guide](DEVELOPMENT.md).

## License

[MIT](LICENSE)
