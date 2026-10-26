#!/bin/sh
# Installs or updates the FlowRate server: installs Docker when it is missing, puts the compose files into the
# install directory, keeps the .env and secrets/ already there, asks for the public IP and the push notifications
# on the first run, gets the images, opens the client ports in ufw or firewalld and starts the stack, waiting
# until both containers are healthy. Run it again to update.
#
# From the unpacked release archive it loads the images of the archive; on its own it downloads the compose files
# of its release from GitHub and pulls the images from ghcr.io:
#   curl -fsSL https://github.com/puffer-bit/FlowRate-Server/releases/latest/download/install.sh | sudo sh
#
# Usage: sh install.sh [options]
#   --public-ip IP          address clients reach this machine at, detected when not given
#   --fcm-key FILE          Firebase service account key (JSON): push notifications straight to FCM
#   --push-relay URL        push notifications through a push relay instead, when there is no key of your own
#   --no-push               push notifications off
#   --network host|bridge   container networking, host on the first run; kept between runs
#   --dir DIR               install directory, /opt/flowrate by default
#   --version TAG           release to install instead of the one this script comes from (v0.3.0), without
#                           the archive only
#   -y, --yes               no questions: install Docker, take the detected IP, open the firewall
#                           (also the case without a terminal)
set -eu

# The release this script belongs to, filled in by publish.sh; empty means the latest release
release=
repo=https://github.com/puffer-bit/FlowRate-Server

say() { printf '%s\n' "$*"; }
die() { printf 'error: %s\n' "$*" >&2; exit 1; }

case "${1:-}" in -h|--help) sed -n '2,21p' "$0" 2>/dev/null || say "see $repo"; exit 0 ;; esac

if [ "$(id -u)" -ne 0 ]; then
    command -v sudo >/dev/null || die "run it as root"
    # Piped into sh there is no file to run again
    [ -f "$0" ] || die "run it as root: ... | sudo sh"
    exec sudo sh "$0" "$@"
fi

# Answers come from the terminal even when the script itself arrives on stdin through curl | sh
tty=
if [ -t 0 ]; then tty=/dev/stdin
elif (: </dev/tty) 2>/dev/null; then tty=/dev/tty; fi

cwd=$(pwd)
src=
[ -f "$0" ] && src=$(cd "$(dirname "$0")" && pwd)
dir=/opt/flowrate
public_ip=
network=
push=
fcm_key=
relay_url=
version=
yes=
[ -n "$tty" ] || yes=1

while [ $# -gt 0 ]; do
    case $1 in
        --public-ip) public_ip=${2:?--public-ip needs an address}; shift ;;
        --fcm-key) fcm_key=${2:?--fcm-key needs a file}; push=fcm; shift ;;
        --push-relay) relay_url=${2:?--push-relay needs a URL}; push=relay; shift ;;
        --no-push) push=off ;;
        --network) network=${2:?--network needs host or bridge}; shift ;;
        --dir) dir=${2:?--dir needs a directory}; shift ;;
        --version) version=${2:?--version needs a release tag}; shift ;;
        -y|--yes) yes=1 ;;
        *) die "unknown option $1, see --help" ;;
    esac
    shift
done
case $network in ''|host|bridge) ;; *) die "--network takes host or bridge, got $network" ;; esac
case $public_ip in *[!0-9a-fA-F.:]*) die "--public-ip takes an IP address, got $public_ip" ;; esac

# From the archive the images are next to the script, otherwise they are pulled
archive=
[ -n "$src" ] && [ -f "$src/images.tar" ] && archive=1
if [ -n "$version" ]; then
    [ -z "$archive" ] || die "--version is for an install without the archive, this archive is $release"
    case $version in v*) release=$version ;; *) release=v$version ;; esac
fi

confirm() {
    [ -n "$yes" ] && return 0
    printf '%s [Y/n] ' "$1"
    read -r answer < "$tty" || answer=
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

# A path given relative to the directory the script was started in
absolute() { case $1 in /*) printf '%s' "$1" ;; *) printf '%s/%s' "$cwd" "$1" ;; esac; }

check_fcm_key() {
    [ -f "$1" ] || die "no Firebase key at $1"
    for field in project_id client_email private_key; do
        grep -q "\"$field\"" "$1" || die "$1 is not a Firebase service account key: it has no $field"
    done
}

check_relay_url() {
    case $1 in http://?*|https://?*) ;; *) die "the push relay URL must start with http:// or https://, got $1" ;; esac
}

get_env() { sed -n "s/^$1=//p" .env | tail -n 1; }
set_env() {
    value=$(printf '%s' "$2" | sed 's/[\\&|]/\\&/g')
    if grep -q "^$1=" .env; then sed -i "s|^$1=.*|$1=$value|" .env
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

# Wrong options fail before anything is installed
[ -z "$fcm_key" ] || { fcm_key=$(absolute "$fcm_key"); check_fcm_key "$fcm_key"; }
[ -z "$relay_url" ] || check_relay_url "$relay_url"

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

# Without the archive the compose files of the release come from GitHub
if [ -z "$archive" ]; then
    if [ -n "$release" ]; then base=$repo/releases/download/$release
    else base=$repo/releases/latest/download; fi
    src=$(mktemp -d)
    trap 'rm -rf "$src"' EXIT
    say "Downloading ${release:-the latest release}..."
    # GitHub drops the leading dot of asset names, so .env.example is published as env.example
    for file in docker-compose.yml docker-compose.bridge.yml env.example README.txt; do
        fetch 60 "$base/$file" > "$src/$file" \
            || die "could not download $base/$file${release:+, is $release a release of $repo?}"
    done
    mv "$src/env.example" "$src/.env.example"
fi

# The install directory; an existing .env and secrets/ stay as they are
mkdir -p "$dir/secrets"
chmod 755 "$dir/secrets"
dir=$(cd "$dir" && pwd)
if [ "$src" != "$dir" ]; then
    cp "$src/docker-compose.yml" "$src/docker-compose.bridge.yml" "$src/.env.example" "$src/README.txt" "$dir/"
fi
cd "$dir"
first_run=
[ -f .env ] || { cp .env.example .env; first_run=1; }

current_ip=$(get_env PUBLIC_IP)
if [ -z "$public_ip" ] && { [ -z "$current_ip" ] || [ "$current_ip" = 127.0.0.1 ]; }; then
    say "Detecting the public IP..."
    detected=$(detect_ip)
    if [ -n "$yes" ]; then
        public_ip=$detected
    else
        printf 'Public IP clients reach this machine at [%s]: ' "$detected"
        read -r public_ip < "$tty" || public_ip=
        public_ip=${public_ip:-$detected}
    fi
    [ -n "$public_ip" ] || die "could not detect the public IP, pass --public-ip"
fi
[ -z "$public_ip" ] || set_env PUBLIC_IP "$public_ip"

# Push notifications: asked on the first run only, the options change them on any run
if [ -z "$push" ] && [ -n "$first_run" ] && [ -z "$yes" ]; then
    say "Push notifications: the path to a Firebase service account key (JSON) to send them to FCM directly,"
    printf 'or the URL of a push relay, empty to leave them off: '
    read -r answer < "$tty" || answer=
    case $answer in
        '') push=off ;;
        http://*|https://*) relay_url=$answer; check_relay_url "$relay_url"; push=relay ;;
        *) fcm_key=$(absolute "$answer"); check_fcm_key "$fcm_key"; push=fcm ;;
    esac
fi
case $push in
    fcm)
        # The container runs as another user, it needs the key readable
        key_name=$(basename "$fcm_key")
        [ "$fcm_key" = "$dir/secrets/$key_name" ] || cp "$fcm_key" "secrets/$key_name"
        chmod 644 "secrets/$key_name"
        set_env PUSH_SERVICE_ACCOUNT_FILE "$key_name"
        set_env PUSH_RELAY_URL "" ;;
    relay)
        set_env PUSH_SERVICE_ACCOUNT_FILE ""
        set_env PUSH_RELAY_URL "$relay_url" ;;
    off)
        set_env PUSH_SERVICE_ACCOUNT_FILE ""
        set_env PUSH_RELAY_URL "" ;;
esac
key_name=$(get_env PUSH_SERVICE_ACCOUNT_FILE)
[ -z "$key_name" ] || [ -f "secrets/$key_name" ] \
    || die "PUSH_SERVICE_ACCOUNT_FILE in $dir/.env names $key_name, which is not in $dir/secrets"

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

if [ -n "$archive" ]; then
    say "Loading the images..."
    docker load -i "$src/images.tar"
    host_arch=$(docker version -f '{{.Server.Arch}}')
    for image in $(docker compose config --images); do
        image_arch=$(docker image inspect -f '{{.Architecture}}' "$image")
        [ "$image_arch" = "$host_arch" ] || die "the archive is built for $image_arch, this machine runs $host_arch"
    done
else
    say "Pulling the images..."
    docker compose pull || die "could not pull the images from ghcr.io"
fi

open_firewall

say "Starting..."
if ! docker compose up -d --remove-orphans --wait; then
    docker compose logs --tail 50
    die "the containers did not become healthy, see the logs above"
fi

public_ip=$(get_env PUBLIC_IP)
if [ -n "$(get_env PUSH_SERVICE_ACCOUNT_FILE)" ]; then push="FCM, key secrets/$(get_env PUSH_SERVICE_ACCOUNT_FILE)"
elif [ -n "$(get_env PUSH_RELAY_URL)" ]; then push="through the relay $(get_env PUSH_RELAY_URL)"
else push="off, sh install.sh --fcm-key FILE turns them on"; fi
cat <<EOF

FlowRate server is running ($network networking), installed in $dir
  Clients connect to   $public_ip:26666
  Open from outside    26666/tcp, 4433/tcp+udp (also on the router or cloud firewall)
  Push notifications   $push
  Control panel        ssh -L 7114:127.0.0.1:7114 root@$public_ip, then https://localhost:7114/
  Logs                 cd $dir && docker compose logs -f
EOF
