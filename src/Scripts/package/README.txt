FlowRate server
===============

The server and its SFU (mediasoup) as Docker images, for Linux on x64 and arm64.

Install
-------
One command, the images come from ghcr.io. The newest pre-release:

    curl -fsSL "$(curl -fsSL https://api.github.com/repos/puffer-bit/FlowRate-Server/releases \
        | grep -o 'https://[^"]*/releases/download/[^"]*/install.sh' | head -n 1)" | sudo sh

or a particular release by its tag:

    curl -fsSL https://github.com/puffer-bit/FlowRate-Server/releases/download/<tag>/install.sh | sudo sh

Or from the release archive, for a machine without access to ghcr.io: unpack it and run the installer
from its folder:

    tar -xzf FlowRateServer-*.tar.gz && cd FlowRateServer-*/
    sh install.sh

It installs Docker when it is missing, puts the compose files and .env into /opt/flowrate, asks for the
public IP (detected for you) and the push notifications, gets the images, opens 26666 and 4433 in ufw or
firewalld when one is active and starts the containers. Options (sh install.sh --help), after sh -s -- when
piped:

    --public-ip IP          skip the question
    --fcm-key FILE          push notifications to FCM with this Firebase key, see below
    --push-relay URL        push notifications through a relay
    --no-push               push notifications off
    --network bridge        bridge networking, see below
    --dir DIR               another install directory
    --version TAG           another release than the latest one, without the archive only
    -y                      no questions at all

    curl -fsSL https://github.com/puffer-bit/FlowRate-Server/releases/download/<tag>/install.sh \
        | sudo sh -s -- --public-ip 203.0.113.5 --fcm-key ./firebase-adminsdk.json

Everything after that runs in the install directory, e.g. cd /opt/flowrate && docker compose logs -f.
Check that the server reached the SFU:

    docker compose logs server | grep gRPC

"Successfully connected to gRPC server" means it did.

Update
------
Run the installer again: the curl command above, or install.sh of a newer archive unpacked anywhere. It
keeps .env and secrets/ in /opt/flowrate and the data in the volumes.

Without the installer: copy .env.example to .env, set PUBLIC_IP, then docker load -i images.tar (or
docker compose pull) and docker compose up -d in this folder.

Ports
-----
Both containers use the network of the host, the ports are opened on the host itself:

    26666/tcp       clients (WebSocket, media, push replies)    open from outside
    4433/tcp+udp    voice and video (WebRTC)                    open from outside
    7114/tcp        control panel                               keep closed
    50051/tcp       server to SFU, 127.0.0.1 only               -

These ports must be free on the host. On OpenWrt open the first two from wan:

    uci add firewall rule
    uci set firewall.@rule[-1].name='Allow-FlowRate'
    uci set firewall.@rule[-1].src='wan'
    uci set firewall.@rule[-1].proto='tcp udp'
    uci set firewall.@rule[-1].dest_port='26666 4433'
    uci set firewall.@rule[-1].target='ACCEPT'
    uci commit firewall && service firewall restart

Control panel
-------------
The panel has no authentication, so it listens on 127.0.0.1 only. Reach it through SSH:

    ssh -L 7114:127.0.0.1:7114 user@server

then open https://localhost:7114/ (the certificate is self-signed). To reach it from the LAN instead,
set PANEL_LISTEN_IP in .env to the LAN address of this machine and run docker compose up -d again.
Never set it to an address reachable from the internet.

Bridge networking
-----------------
Where the containers must not share the network of the host (Docker Desktop, for one):

    sh install.sh --network bridge

It stores COMPOSE_FILE in .env, so plain docker compose commands keep using the bridge override.

WebRTC then goes through Docker NAT, and a host firewall has to let the traffic of the compose bridge
through: on OpenWrt that is a forward rule from wan to the zone of the bridge.

PostgreSQL (optional)
---------------------
SQLite is the default. For a new PostgreSQL database, set POSTGRES_PASSWORD in .env
and add this line (host networking):

    COMPOSE_FILE=docker-compose.yml:docker-compose.postgresql.yml

For bridge networking:

    COMPOSE_FILE=docker-compose.yml:docker-compose.bridge.yml:docker-compose.postgresql.yml:docker-compose.postgresql.bridge.yml

Then run docker compose up -d. PostgreSQL uses a separate postgres-data volume.
Host mode publishes the database only on 127.0.0.1:${POSTGRES_PORT:-5432}; bridge mode
publishes no database port. The installer preserves this selection during updates.
The password initializes a new volume; changing .env later does not change an existing
PostgreSQL user's password. Selecting PostgreSQL does not import existing SQLite data automatically. To transfer it,
stop the SQLite server before enabling these overrides and back up server-data. Start
only PostgreSQL, then run the offline command before starting the regular server:

    docker compose up -d --wait postgres
    docker compose run --rm --no-deps server import-sqlite --source /data/server.db --dry-run
    docker compose run --rm --no-deps server import-sqlite --source /data/server.db
    docker compose up -d server

Keep the original Compose project name and server-data volume. The importer requires
SQLite at the same server schema version and an empty PostgreSQL target. Dry run
initializes the PostgreSQL schema, verifies the complete import, and rolls back rows
and sequences. The source is read-only. IDs, links, and credentials are preserved;
media/certificates remain in server-data. Do not start the normal PostgreSQL server
before importing, as it seeds a new installation and the import will refuse it.
Include both the PostgreSQL volume and server-data (media/certificates) in backups.

Push notifications
------------------
Off unless the first run of the installer was given a key or a relay. To send them to FCM directly, with
the Firebase service account key (Firebase console, Project settings, Service accounts, Generate new
private key):

    sh install.sh --fcm-key firebase-adminsdk.json

It copies the key into /opt/flowrate/secrets/, readable by the container, and names it in .env. Without a
key of your own, a push relay:

    sh install.sh --push-relay https://relay.example.com

and sh install.sh --no-push turns them off again. By hand: put the key into secrets/ (chmod 644) and set
PUSH_SERVICE_ACCOUNT_FILE=<its name> in .env, or set PUSH_RELAY_URL, then docker compose up -d again.

Data
----
The SQLite database, generated certificate and uploaded media live in flowrate_server-data;
logs live in flowrate_server-logs. With PostgreSQL, the database uses flowrate_postgres-data
and media/certificates still use server-data. Updates keep the volumes; docker compose down -v
deletes the volumes selected by COMPOSE_FILE.
