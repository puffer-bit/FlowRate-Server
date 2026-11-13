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

Personal chats and groups belong to their coordinator and use persistent membership, separate from
server channels and role permissions. Only participants receive their messages and attachments;
server administration permissions do not grant access to personal conversations. A compatible
protocol 5 client is required. Existing text chats remain server channels after the database migration.
Membership is stored in `ChatSessionMembers`, one row per participant, with indexes for both chat and
user lookups. Chat lists load membership in a single database query. Membership updates retain unchanged
rows and their joining dates. The migration transfers existing JSON membership before removing the old
column; existing participants receive the migration time as their joining date. Deleting a chat removes
its membership rows, while deleting an account retains its historical participant id and clears the
nullable account reference so that private chat history remains loadable.

## First launch

You need a Linux machine (x64 or arm64), SSH access, and an IP address that your clients can reach.
The stack includes two containers: `server` (chat server and control panel) and `mediasoup` (the SFU,
which forwards voice, video and screen sharing). A separate database installation is not required.

### Install a release

Run this on the machine that will host FlowRate. It installs the newest pre-release using images from ghcr.io:

```sh
curl -fsSL "$(curl -fsSL https://api.github.com/repos/puffer-bit/FlowRate-Server/releases \
    | grep -o 'https://[^"]*/releases/download/[^"]*/install.sh' | head -n 1)" | sudo sh
```

To install a particular [release](https://github.com/puffer-bit/FlowRate-Server/releases), replace `<tag>`:

```sh
curl -fsSL https://github.com/puffer-bit/FlowRate-Server/releases/download/<tag>/install.sh | sudo sh
```

The installer:

1. Offers to install Docker if it is missing; the Docker Compose plugin must also be available.
2. Creates `/opt/flowrate` with the Compose files, `.env` and `secrets/`.
3. Detects the public IP and asks you to confirm it. For a LAN-only server, enter the machine's LAN IP instead.
4. Asks about push notifications. Leave the answer empty to keep them off for now.
5. Downloads the images, offers to open the client ports in an active ufw or firewalld, and starts both containers.

You can supply the address and skip push setup explicitly:

```sh
curl -fsSL https://github.com/puffer-bit/FlowRate-Server/releases/download/<tag>/install.sh \
    | sudo sh -s -- --public-ip 203.0.113.5 --no-push
```

`203.0.113.5` is an example: replace it with your actual IP. Use `--dir /your/path` to change the installation
directory. Each release also ships an archive with the images for machines without access to ghcr.io;
see the [release package instructions](src/Scripts/package/README.txt) for installation and other flags.

### Build from source instead

With Git, Docker and the Docker Compose plugin installed:

```sh
git clone --recurse-submodules https://github.com/puffer-bit/FlowRate-Server.git
cd FlowRate-Server
cp .env.example .env
mkdir -p secrets
```

Edit `.env` and set `PUBLIC_IP` as described below, then build and start:

```sh
docker compose up -d --build
```

For an existing checkout, run `git submodule update --init --recursive` before building.

### Check the first start

Run Compose commands from the installation directory: `/opt/flowrate` for the installer, or the repository
root for a source build. Add `sudo` to Docker commands if your account requires it.

```sh
cd /opt/flowrate
docker compose ps
docker compose logs --tail 100 server mediasoup
```

Both services should become healthy. The server log message `Connection with SFU node <id> is up.`
(with the actual node ID) confirms the connection to the SFU. On the first start, the server creates its database, a self-signed panel
certificate, a local SFU node, and a default coordinator listening on `0.0.0.0:26666`.
A coordinator is the server instance that clients connect to; it owns its users, chats and sessions.

Open the panel as described below, then connect a FlowRate client to `<server-ip>:26666`.
For a remote installation, `127.0.0.1` points to the client's own machine, so use the server's reachable IP.
Test both a text message and a voice session to check the client and media connections.

## Configure `.env`

The `.env` file lives next to `docker-compose.yml`: `/opt/flowrate/.env` after installation, or `.env` in
the repository root for a source build. The installer creates it for you; for a manual setup, copy
[`.env.example`](.env.example). This file configures Docker Compose; a server started directly with
`dotnet` does not read it automatically (see the [development guide](DEVELOPMENT.md)).

For a basic server, set `PUBLIC_IP` and leave the other values at their defaults:

```dotenv
PUBLIC_IP=203.0.113.5
RTC_LISTEN_IP=
SFU_GRPC_USERNAME=
SFU_GRPC_PASSWORD=
PANEL_LISTEN_IP=127.0.0.1
PUSH_SERVICE_ACCOUNT_FILE=
PUSH_RELAY_URL=
PASSKEY_DOMAIN=
```

| Variable | Default in `.env.example` | What to set |
| --- | --- | --- |
| `PUBLIC_IP` | `127.0.0.1` (local testing only) | IP address clients reach the SFU at, without a scheme or port. Use the public IP for internet access, or the machine's LAN IP for LAN-only access. The installer asks for this value. |
| `RTC_LISTEN_IP` | Empty (all interfaces) | Local address the SFU binds for media. Usually leave empty. Only set an address actually assigned to this machine; see the network notes below. Ignored in bridge mode. |
| `SFU_GRPC_USERNAME` | Empty | Optional username for the local SFU's internal API. Set together with the password; no `:` or surrounding whitespace. |
| `SFU_GRPC_PASSWORD` | Empty | Optional strong password for that API. Both credentials empty disables authentication; setting only one prevents startup. Existing nodes also need matching credentials in the panel. |
| `PANEL_LISTEN_IP` | `127.0.0.1` | Local address for the control panel on port `7114`. Keep the default for SSH access, or use a trusted LAN interface address. The panel has no authentication. |
| `PUSH_SERVICE_ACCOUNT_FILE` | Empty | Filename of a Firebase service account JSON key inside `secrets/`, for example `firebase-adminsdk.json`. Use only the filename, not a host path. |
| `PUSH_RELAY_URL` | Empty | HTTP(S) URL of a push relay, for example `https://relay.example.com`. Leave the key filename empty when using a relay. Both empty disables push notifications. |
| `PASSKEY_DOMAIN` | Empty | Domain for passkey login, for example `chat.example.com`, without a scheme or port. Empty disables passkeys. See the setup steps below before enabling it. |

After editing `.env`, apply the changes from the same directory:

```sh
docker compose up -d
docker compose ps
```

Compose recreates the affected containers. `docker compose restart` alone does not apply changed environment
variables. Recreating the containers can briefly interrupt connections and media.

Server names, connection passwords, user limits, roles, chats and SFU assignments are managed in the panel
and stored in the database. They are not `.env` settings. SFU credentials are a special case: `.env` configures
the SFU itself and seeds the server's node credentials only on the first launch; see [SFU authentication](#sfu-grpc-authentication).

## Network and ports

| Port | Purpose | Access |
| --- | --- | --- |
| `26666/tcp` | Client connections, uploads and downloads | Allow from client networks |
| `4433/tcp` and `4433/udp` | Voice, video and screen sharing (WebRTC) | Allow both from client networks |
| `7114/tcp` | Control panel | Keep private; localhost by default |
| `50051/tcp` | Server-to-SFU gRPC API | Loopback only in host mode; internal container network in bridge mode |

The default Compose setup uses the host's network. These ports must be free on the machine.
The installer handles active ufw or firewalld rules with your confirmation; configure any cloud firewall
or router separately. Behind a router, forward `26666/tcp` and `4433/tcp+udp` to the machine running FlowRate.
`PUBLIC_IP` only tells clients where to send media; it does not create port forwarding or open a firewall.

Leave `RTC_LISTEN_IP` empty when the public IP belongs to your router rather than the server itself.
If the public IP is assigned directly to the server and clients also reach it through another interface
(for example, the server is also the LAN router), setting `RTC_LISTEN_IP` to that public IP can prevent
UDP replies from using the wrong source address and forcing media to fall back to TCP.

### Bridge mode

For Docker Desktop or environments where containers must use a separate network, the installer accepts
`--network bridge`. For a manual installation, add this line to `.env` before starting:

```dotenv
COMPOSE_FILE=docker-compose.yml:docker-compose.bridge.yml
```

Then use the usual `docker compose up -d` command. Compose will load both files for later commands too.
In this mode the SFU binds inside its container, so `RTC_LISTEN_IP` is ignored. WebRTC traffic goes through
Docker NAT; the host firewall must allow forwarded traffic to the Docker bridge.

## Open the control panel and finish setup

The panel has **no authentication** and listens on `127.0.0.1` by default. Keep it private.
On your own computer, open an SSH tunnel to the server and leave it running:

```sh
ssh -N -L 7114:127.0.0.1:7114 user@server
```

Replace `user@server` with your SSH login and server address. Open **https://localhost:7114/** in a browser.
The certificate is self-signed on first start, so the browser will show a certificate warning.
For a server running on your own computer, open that URL directly without a tunnel.

In the panel:

1. Open **SFU nodes** and check that the `local` node is connected.
2. Open **Coordinators**, select the default coordinator, then open **Settings**.
3. Set its name, an optional connection password, and user limits (initially 64 online users and 256 total users).
4. Adjust the welcome message, access lists, file limits and assigned SFU nodes if needed, then save.

The coordinator's `0.0.0.0` address means it listens on all interfaces; clients connect using the machine's
reachable IP or domain. Changing the coordinator's listen address or port restarts it and disconnects users.
If you change its port, update the firewall and, in bridge mode, the Compose port mapping as well;
the supplied server health check also expects port `26666`.

To access the panel from a trusted LAN instead of SSH, set `PANEL_LISTEN_IP` to the server's LAN IP and
run `docker compose up -d`. Never expose this panel to the internet.

## Optional features

### Push notifications

Choose one delivery method:

- **Direct Firebase delivery:** put the service account JSON key in `secrets/firebase-adminsdk.json` and set
  `PUSH_SERVICE_ACCOUNT_FILE=firebase-adminsdk.json`. The container must be able to read the file;
  the installer uses mode `644`, so keep the host and the secrets directory restricted to trusted users.
- **Relay:** set `PUSH_RELAY_URL=https://your-relay.example.com` and leave `PUSH_SERVICE_ACCOUNT_FILE` empty.

Leave the unused setting empty and run `docker compose up -d`. If both are set, the Firebase key takes
precedence. To disable push, clear both values and apply again. The installer also accepts `--fcm-key FILE`,
`--push-relay URL` and `--no-push`, including on subsequent runs.

### Passkeys (FIDO2)

1. Choose the domain users will reach the server by, for example `chat.example.com`, and configure its DNS.
2. Set `PASSKEY_DOMAIN=chat.example.com` and run `docker compose up -d`.
3. To add passkeys in the panel through SSH, map that domain to `127.0.0.1` in your computer's hosts file,
   keep the SSH tunnel open, and visit `https://chat.example.com:7114/`.
4. Add a passkey through the user's security settings in the panel.

The panel must be opened with that domain for passkey registration; `https://localhost:7114/` will not match.
Changing `PASSKEY_DOMAIN` later makes previously registered passkeys unusable for that domain.

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

## Updates, logs and data

For a release installation, run the installer again with the desired release tag. It keeps the existing
`.env`, `secrets/` and Docker data volumes. For a source build, update the checkout and submodules, then run
`docker compose up -d --build` again. Compare the updated `.env.example` with your `.env` for new options;
keep your own addresses and secrets.

Useful commands from the installation directory:

```sh
docker compose ps                           # container state and health
docker compose logs -f --tail 100            # follow logs; Ctrl+C exits the log view
docker compose logs --tail 100 mediasoup     # SFU startup and media errors
docker compose down                         # stop and remove containers; keep data
```

The `flowrate_server-data` volume contains the database, uploaded media and generated certificate;
`flowrate_server-logs` contains log files. Back up the data volume together with `.env` and `secrets/`.
Stop the stack before copying the database volume for a consistent backup. **`docker compose down -v`
deletes the volumes, including users, settings and uploaded files.**

Nothing secret belongs in the repository. `.env` and `secrets/` are git-ignored; protect their contents
and your backups.

### If something does not work

| Symptom | What to check |
| --- | --- |
| Compose says `set PUBLIC_IP` | Create `.env` next to `docker-compose.yml`, set a nonempty `PUBLIC_IP`, and run Compose from that directory. |
| A container exits or stays unhealthy | Check `docker compose logs --tail 100 server mediasoup`. Verify that the required ports are free and any configured Firebase key exists and is readable. |
| The client cannot connect | Use the server's reachable IP and coordinator port (initially `26666`); check the coordinator state, TCP firewall rules and router forwarding. |
| Text works, but voice or video does not | Check `PUBLIC_IP`, both TCP and UDP `4433`, router forwarding, and the local SFU's connection and assignment in the panel. |
| The panel does not open | Keep the SSH tunnel running, use `https://localhost:7114/`, check the certificate warning and whether local port `7114` is already in use. |
| SFU stops connecting after credentials change | Set both credentials in `.env` and matching credentials on the existing node in the panel. Environment changes do not overwrite stored node credentials. |
| An edited `.env` seems to have no effect | Run `docker compose up -d` from the installation directory. For settings stored in the database, use the panel. |

For source builds, tests, and repository layout, see the [development guide](DEVELOPMENT.md).

## License

[MIT](LICENSE)
