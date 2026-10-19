# Builds the Native AOT probe for the target architecture and runs it on the .NET runtime-deps image.
# Build from the repository root:
#   docker build -f tools/SiameseCat.AotProbe/Dockerfile -t siamesecat-aot-probe .
#   docker buildx build --platform linux/arm64 -f tools/SiameseCat.AotProbe/Dockerfile -t siamesecat-aot-probe:arm64 .

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build

ARG TARGETARCH

RUN apt-get update \
    && apt-get install -y --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src

COPY Directory.Build.props Directory.Packages.props README.md LICENSE ./
COPY src/ src/
COPY tools/SiameseCat.AotProbe/ tools/SiameseCat.AotProbe/

RUN arch="${TARGETARCH:-$(uname -m)}"; \
    case "$arch" in \
        amd64|x86_64) rid="linux-x64" ;; \
        arm64|aarch64) rid="linux-arm64" ;; \
        *) echo "Unsupported TARGETARCH: $arch" >&2; exit 1 ;; \
    esac; \
    dotnet publish tools/SiameseCat.AotProbe/SiameseCat.AotProbe.csproj \
        --configuration Release \
        --runtime "$rid" \
        -p:PublishAot=true \
        --output /app

FROM mcr.microsoft.com/dotnet/runtime-deps:8.0

WORKDIR /app
COPY --from=build /app/ ./

ENTRYPOINT ["./SiameseCat.AotProbe"]
