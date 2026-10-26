#!/usr/bin/env bash
# Builds the Docker images of the server (with the control panel) and the SFU from docker-compose.yml, names
# them after the release (ghcr.io/puffer-bit/flowrate-server:<release without v>) and packs everything a machine
# needs to run them into dist/FlowRateServer-<version>-linux-<arch>-DOCKER.tar.gz, one FlowRateServer-<version>
# folder with the images, the compose files without their build sections, an example .env, the installer and
# a README. There, inside the unpacked folder:
#   sh install.sh
# The same files without the images go to dist/install/, the release assets install.sh downloads when it runs
# without the archive.
#
# Usage: ./publish.sh                 build and pack
#        ./publish.sh --no-cache      anything else goes to docker compose build as is
# RELEASE_TAG names the release (v<version> by default), IMAGE_REGISTRY the registry (ghcr.io/puffer-bit).
set -euo pipefail

script=$(readlink -f "$0")
cd "$(dirname "$script")/../../.."

PROJECT=src/Server/Server.Asp/Server.Asp.csproj
PACKAGE=src/Scripts/package

case "${1:-}" in -h|--help) sed -n '2,13p' "$script"; exit 0 ;; esac

command -v docker >/dev/null || { echo "error: docker is not installed" >&2; exit 1; }
# PUBLIC_IP matters only when the stack runs, compose demands it for any command
export PUBLIC_IP=${PUBLIC_IP:-127.0.0.1}
docker compose build "$@"

version=$(sed -n 's:.*<InformationalVersion>\(.*\)</InformationalVersion>.*:\1:p' "$PROJECT" | head -n1)
[[ -n $version ]] || { echo "error: no <InformationalVersion> in $PROJECT" >&2; exit 1; }
release=${RELEASE_TAG:-v$version}
registry=${IMAGE_REGISTRY:-ghcr.io/puffer-bit}
# The images are built for the Docker host: amd64 becomes x64 as in the client archives
arch=$(docker image inspect -f '{{.Architecture}}' flowrate/server:latest)
[[ $arch == amd64 ]] && arch=x64
name="FlowRateServer-$version"
archive="dist/$name-linux-$arch-DOCKER.tar.gz"

staging=$(mktemp -d)
trap 'rm -rf "$staging"' EXIT

# flowrate/server:latest becomes $registry/flowrate-server:<release>, in the images and in the compose file
rename=()
images=()
for image in $(docker compose config --images); do
    repository=${image%:*}
    target="$registry/${repository//\//-}:${release#v}"
    docker tag "$image" "$target"
    rename+=(-e "s|image: $image\$|image: $target|")
    images+=("$target")
done
docker save -o "$staging/images.tar" "${images[@]}"
chmod 644 "$staging/images.tar"

# The build sections point into src/, which the target machine does not have
awk '/^    build:/ { skip = 1; next } skip && /^      / { next } { skip = 0; print }' docker-compose.yml \
    | sed "${rename[@]}" > "$staging/docker-compose.yml"

cp docker-compose.bridge.yml "$PACKAGE/README.txt" "$staging/"
sed "s|^release=\$|release=$release|" "$PACKAGE/install.sh" > "$staging/install.sh"
chmod 755 "$staging/install.sh"
# Not .env: unpacking a newer archive over an installed one must not replace the settings there
cp "$PACKAGE/env" "$staging/.env.example"

rm -rf dist/install
mkdir -p dist/install
tar -czf "$archive" -C "$staging" --transform "s,^,$name/," images.tar docker-compose.yml docker-compose.bridge.yml .env.example install.sh README.txt
# GitHub drops the leading dot of asset names
cp "$staging/docker-compose.yml" "$staging/docker-compose.bridge.yml" "$staging/install.sh" "$staging/README.txt" dist/install/
cp "$staging/.env.example" dist/install/env.example
echo "Images:  ${images[*]}"
echo "Archive: $archive ($(du -h "$archive" | cut -f1))"
