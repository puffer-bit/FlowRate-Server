#!/usr/bin/env bash
# Builds the server and starts it from Server/Server.Asp, as the IDE does. The SFU is not started here:
# run it from Sfu/mediasoup (npm run dev) or with docker compose.
#
# Usage: ./run.sh                     Debug build
#        ./run.sh -c Release          another configuration
#        ./run.sh --panel             build the control panel first (npm ci on the first run)
#        ./run.sh -- <arguments>      everything after -- goes to the server
set -euo pipefail

script=$(readlink -f "$0")
cd "$(dirname "$script")/../.."

PROJECT=Server/Server.Asp
PANEL=Web/admin-panel

configuration=Debug
panel=false
while (( $# )); do
    case "$1" in
        -c|--configuration) configuration=${2:-}; shift ;;
        --panel) panel=true ;;
        -h|--help) sed -n '2,7p' "$script"; exit 0 ;;
        --) shift; break ;;
        *) echo "Unknown option: $1" >&2; exit 1 ;;
    esac
    shift
done

command -v dotnet >/dev/null || { echo "error: dotnet is not installed" >&2; exit 1; }
if [[ $panel == true ]]; then
    command -v npm >/dev/null || { echo "error: npm is not installed" >&2; exit 1; }
    [[ -d $PANEL/node_modules ]] || npm --prefix "$PANEL" ci
    npm --prefix "$PANEL" run build
fi
dotnet build "$PROJECT" -c "$configuration" -nologo -v quiet -nodeReuse:false -p:UseSharedCompilation=false
cd "$PROJECT"
exec dotnet run -c "$configuration" --no-build -- "$@"
