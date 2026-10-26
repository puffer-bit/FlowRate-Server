FlowRate server
===============

The server and its SFU (mediasoup) as Docker images, for Linux.

Install
-------
Unpack the archive and run the installer from its folder:

    tar -xzf FlowRateServer-*.tar.gz && cd FlowRateServer-*/
    sh install.sh

It installs Docker when it is missing, puts the compose files and .env into /opt/flowrate, asks for the
public IP (detected for you), loads the images, opens 26666 and 4433 in ufw or firewalld when one is
active and starts the containers. Options (sh install.sh --help):

    --public-ip IP          skip the question
    --network bridge        bridge networking, see below
    --dir DIR               another install directory
    -y                      no questions at all

Everything after that runs in the install directory, e.g. cd /opt/flowrate && docker compose logs -f.
Check that the server reached the SFU:

    docker compose logs server | grep gRPC

"Successfully connected to gRPC server" means it did.

Update
------
Unpack the newer archive anywhere and run its install.sh again. It keeps .env and secrets/ in
/opt/flowrate and the data in the volumes.

Without the installer: copy .env.example to .env, set PUBLIC_IP, then docker load -i images.tar and
docker compose up -d in this folder.

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

Push notifications
------------------
Off by default. To send them to FCM directly, put the Firebase service account key into secrets/ of the
install directory, readable by the container (chmod 644), and name it in .env:

    cp firebase-adminsdk.json /opt/flowrate/secrets/ && chmod 644 /opt/flowrate/secrets/firebase-adminsdk.json
    PUSH_SERVICE_ACCOUNT_FILE=firebase-adminsdk.json

Without a key of your own, set PUSH_RELAY_URL to a push relay instead. Then docker compose up -d again.

Data
----
The database, the generated certificate and the uploaded media live in the volume flowrate_server-data,
the logs in flowrate_server-logs. Updates keep them, docker compose down -v in /opt/flowrate deletes them.
