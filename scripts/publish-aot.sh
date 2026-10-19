#!/usr/bin/env bash
# Publishes the SiameseCat Native AOT probe, including cross-OS targets.
#
# Cross-compilation is provided by the PublishAotCross.Tools package (Zig for Linux/macOS,
# xwin for Windows), so every supported runtime identifier can be produced from any host.
#
# Usage:
#   ./scripts/publish-aot.sh                    # host RID
#   ./scripts/publish-aot.sh all                # all supported RIDs
#   ./scripts/publish-aot.sh linux-x64 out      # explicit RID and output folder
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$root/tools/SiameseCat.AotProbe/SiameseCat.AotProbe.csproj"
supported=(win-x64 win-arm64 linux-x64 linux-arm64 linux-musl-x64 linux-musl-arm64 osx-x64 osx-arm64)

host_arch="$(uname -m)"
case "$host_arch" in
    x86_64|amd64) host_arch="x64" ;;
    aarch64|arm64) host_arch="arm64" ;;
esac

case "$(uname -s)" in
    Linux*) host_os="linux" ;;
    Darwin*) host_os="osx" ;;
    *) echo "Unsupported operating system: $(uname -s)" >&2; exit 1 ;;
esac

host_rid="$host_os-$host_arch"
target="${1:-$host_rid}"

publish() {
    local rid="$1"
    local output="$2"

    echo "Publishing Native AOT for $rid -> $output"
    dotnet publish "$project" \
        --configuration Release \
        --runtime "$rid" \
            -p:PublishAotCrossDebug=false \
        --output "$output"
}

if [ "$target" = "all" ]; then
    for rid in "${supported[@]}"; do
        publish "$rid" "$root/artifacts/aot/$rid"
    done
    exit 0
fi

if [[ ! " ${supported[*]} " =~ " $target " ]]; then
    echo "Unsupported runtime identifier '$target'." >&2
    echo "Supported: ${supported[*]}" >&2
    exit 1
fi

output="${2:-$root/artifacts/aot/$target}"
publish "$target" "$output"

if [ "$target" = "$host_rid" ]; then
    "$output/SiameseCat.AotProbe"
fi
