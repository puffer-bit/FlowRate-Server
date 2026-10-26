#!/bin/sh
# Installs or updates the FlowRate server from this unpacked archive: installs Docker when it is missing,
# copies the compose files to the install directory, keeps the .env and secrets/ already there, asks for
# the public IP on the first run, loads the images, opens the client ports in ufw or firewalld and starts
# the stack, waiting until both containers are healthy. Run it again from a newer archive to update.
#
# Usage: sh install.sh [options]
#   --public-ip IP          address clients reach this machine at, detected when not given
#   --network host|bridge   container networking, host on the first run; kept between runs
#   --dir DIR               install directory, /opt/flowrate by default
#   -y, --yes               no questions: install Docker, take the detected IP, open the firewall
#                           (also the case without a terminal)
set -eu

say() { printf '%s\n' "$*"; }
die() { printf 'error: %s\n' "$*" >&2; exit 1; }

case "${1:-}" in -h|--help) sed -n '2,12p' "$0"; exit 0 ;; esac

if [ "$(id -u)" -ne 0 ]; then
    command -v sudo >/dev/null || die "run it as root"
    exec sudo sh "$0" "$@"
fi

src=$(cd "$(dirname "$0")" && pwd)
dir=/opt/flowrate
public_ip=
network=
yes=
[ -t 0 ] || yes=1

while [ $# -gt 0 ]; do
    case $1 in
        --public-ip) public_ip=${2:?--public-ip needs an address}; shift ;;
        --network) network=${2:?--network needs host or bridge}; shift ;;
        --dir) dir=${2:?--dir needs a directory}; shift ;;
        -y|--yes) yes=1 ;;
        *) die "unknown option $1, see --help" ;;
    esac
    shift
done
case $network in ''|host|bridge) ;; *) die "--network takes host or bridge, got $network" ;; esac
case $public_ip in *[!0-9a-fA-F.:]*) die "--public-ip takes an IP address, got $public_ip" ;; esac
[ -f "$src/images.tar" ] || die "no images.tar next to install.sh, run it from the unpacked archive"

confirm() {
    [ -n "$yes" ] && return 0
    printf '%s [Y/n] ' "$1"
    read -r answer || answer=
    case $answer in [nN]*) return 1 ;; esac
}

# fetch SECONDS URL
fetch() {
    if command -v curl >/dev/null; then curl -fsSL --max-time "$1" "$2"
    elif command -v wget >/dev/null; then wget -qO- -T "$1" "$2"
    else return 1; fi
}

detect_ip() {
    for url in https://api.ipify.org https://ifconfig.me/ip https://icanhazip.com; do
        addr=$(fetch 5 "$url" 2>/dev/null | tr -d '[:space:]') || continue
        case $addr in ''|*[!0-9.]*) continue ;; esac
        printf '%s' "$addr"
        return
    done
    # Offline: the address of the default route, right for a LAN-only server
    ip -4 route get 1.1.1.1 2>/dev/null | sed -n 's/.* src \([0-9.]*\).*/\1/p'
}

get_env() { sed -n "s/^$1=//p" .env | tail -n 1; }
set_env() {
    if grep -q "^$1=" .env; then sed -i "s|^$1=.*|$1=$2|" .env
    else printf '%s=%s\n' "$1" "$2" >> .env; fi
}

open_firewall() {
    if command -v ufw >/dev/null && ufw status 2>/dev/null | grep -q '^Status: active'; then
        confirm "Open 26666/tcp and 4433/tcp+udp in ufw?" || return 0
        ufw allow 26666/tcp >/dev/null && ufw allow 4433/tcp >/dev/null && ufw allow 4433/udp >/dev/null
    elif command -v firewall-cmd >/dev/null && firewall-cmd --state >/dev/null 2>&1; then
        confirm "Open 26666/tcp and 4433/tcp+udp in firewalld?" || return 0
        firewall-cmd --permanent --add-port=26666/tcp --add-port=4433/tcp --add-port=4433/udp >/dev/null
        firewall-cmd --reload >/dev/null
    fi
}

# Docker and the compose plugin
if ! command -v docker >/dev/null; then
    [ "$(uname -s)" = Linux ] || die "install Docker and run this again"
    confirm "Docker is not installed. Install it with the official script from get.docker.com?" \
        || die "install Docker and run this again"
    script=$(mktemp)
    fetch 120 https://get.docker.com > "$script" || { rm -f "$script"; die "could not download get.docker.com"; }
    sh "$script" || { rm -f "$script"; die "the Docker install script failed"; }
    rm -f "$script"
    if command -v systemctl >/dev/null; then systemctl enable --now docker; fi
fi
if ! docker info >/dev/null 2>&1; then
    if command -v systemctl >/dev/null; then systemctl start docker || true
    elif command -v service >/dev/null; then service docker start || true; fi
    docker info >/dev/null 2>&1 || die "the Docker daemon is not running"
fi
docker compose version >/dev/null 2>&1 \
    || die "the Docker compose plugin is missing: docker-compose-plugin from the Docker repository, docker-compose-v2 on Ubuntu"

# The install directory; an existing .env and secrets/ stay as they are
mkdir -p "$dir/secrets"
chmod 755 "$dir/secrets"
dir=$(cd "$dir" && pwd)
if [ "$src" != "$dir" ]; then
    cp "$src/docker-compose.yml" "$src/docker-compose.bridge.yml" "$src/.env.example" "$src/README.txt" "$dir/"
fi
cd "$dir"
[ -f .env ] || cp .env.example .env

current_ip=$(get_env PUBLIC_IP)
if [ -z "$public_ip" ] && { [ -z "$current_ip" ] || [ "$current_ip" = 127.0.0.1 ]; }; then
    say "Detecting the public IP..."
    detected=$(detect_ip)
    if [ -n "$yes" ]; then
        public_ip=$detected
    else
        printf 'Public IP clients reach this machine at [%s]: ' "$detected"
        read -r public_ip || public_ip=
        public_ip=${public_ip:-$detected}
    fi
    [ -n "$public_ip" ] || die "could not detect the public IP, pass --public-ip"
fi
[ -z "$public_ip" ] || set_env PUBLIC_IP "$public_ip"

# Compose reads COMPOSE_FILE from .env, so plain docker compose commands in $dir use the same networking
if [ -z "$network" ]; then
    case $(get_env COMPOSE_FILE) in *bridge*) network=bridge ;; *) network=host ;; esac
fi
if [ "$network" = bridge ]; then set_env COMPOSE_FILE docker-compose.yml:docker-compose.bridge.yml
else sed -i '/^COMPOSE_FILE=/d' .env; fi

# A running stack holds its own ports, so only a fresh start is checked
if [ -z "$(docker compose ps -q 2>/dev/null)" ] && command -v ss >/dev/null; then
    busy=
    tcp="26666 4433 7114"
    [ "$network" = bridge ] || tcp="$tcp 50051"
    for port in $tcp; do
        [ -z "$(ss -Hltn "sport = :$port")" ] || busy="$busy $port/tcp"
    done
    [ -z "$(ss -Hlun "sport = :4433")" ] || busy="$busy 4433/udp"
    [ -z "$busy" ] || die "ports already in use:$busy"
fi

say "Loading the images..."
docker load -i "$src/images.tar"
image_arch=$(docker image inspect -f '{{.Architecture}}' flowrate/server:latest)
host_arch=$(docker version -f '{{.Server.Arch}}')
[ "$image_arch" = "$host_arch" ] || die "the archive is built for $image_arch, this machine runs $host_arch"

open_firewall

say "Starting..."
if ! docker compose up -d --remove-orphans --wait; then
    docker compose logs --tail 50
    die "the containers did not become healthy, see the logs above"
fi

public_ip=$(get_env PUBLIC_IP)
cat <<EOF

FlowRate server is running ($network networking), installed in $dir
  Clients connect to   $public_ip:26666
  Open from outside    26666/tcp, 4433/tcp+udp (also on the router or cloud firewall)
  Control panel        ssh -L 7114:127.0.0.1:7114 root@$public_ip, then https://localhost:7114/
  Logs                 cd $dir && docker compose logs -f
EOF
