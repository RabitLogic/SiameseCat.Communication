#!/usr/bin/env bash
# Runs a cross-compiled Linux AOT probe inside Docker.
#
# Usage:
#   ./scripts/run-aot-docker.sh linux-x64 [output-folder]
#   ./scripts/run-aot-docker.sh linux-musl-arm64 [output-folder]
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
rid="${1:-}"
output="${2:-$root/artifacts/aot/$rid}"

case "$rid" in
    linux-x64|linux-arm64) image="mcr.microsoft.com/dotnet/runtime-deps:8.0" ;;
    linux-musl-x64|linux-musl-arm64) image="mcr.microsoft.com/dotnet/runtime-deps:8.0-alpine" ;;
    *)
        echo "Usage: $0 <linux-x64|linux-arm64|linux-musl-x64|linux-musl-arm64> [output-folder]" >&2
        exit 1
        ;;
esac

if [ ! -f "$output/SiameseCat.AotProbe" ]; then
    echo "The AOT probe was not found in '$output'. Publish it first." >&2
    exit 1
fi

platform="linux/amd64"
if [[ "$rid" == *arm64 ]]; then
    platform="linux/arm64"
fi

echo "Running $rid probe in $image ($platform)"
docker run --rm --platform "$platform" \
    --mount "type=bind,source=$(cd "$output" && pwd),target=/app,readonly" \
    --workdir /app "$image" ./SiameseCat.AotProbe
