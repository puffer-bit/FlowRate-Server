#!/usr/bin/env bash
# Removes everything dotnet builds: the bin/ and obj/ folders of every project in src/. Stops the
# build servers first, they keep files of obj/ open. The next build starts with a full restore.
#
# Usage: ./clean.sh
#
# Only folders next to a .csproj are removed: node_modules/ of the panel and the SFU stay as they are,
# and so do the server data and dist/.
set -euo pipefail

cd "$(dirname "$(readlink -f "$0")")/../.."

command -v dotnet >/dev/null && dotnet build-server shutdown >/dev/null 2>&1 || true

count=0
while IFS= read -r -d '' project; do
    for folder in "$(dirname "$project")"/{bin,obj}; do
        [[ -d "$folder" ]] || continue
        rm -rf "$folder"
        count=$((count + 1))
    done
done < <(find . -name '*.csproj' -not -path '*/node_modules/*' -print0)
echo "Removed $count folders"
