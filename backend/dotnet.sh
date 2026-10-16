#!/usr/bin/env sh
# Runs the .NET 8 SDK in a container, for machines without it installed:
#   ./dotnet.sh build
#   ./dotnet.sh test          (integration tests start Postgres through the host's Docker)
# The Docker socket is mounted so Testcontainers can start sibling containers.
set -e
cd "$(dirname "$0")"
MSYS_NO_PATHCONV=1 exec docker run --rm -i \
  -v "$(pwd -W 2>/dev/null || pwd):/src" -w /src \
  -v mini-mes-nuget:/root/.nuget/packages \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
  --add-host host.docker.internal:host-gateway \
  mcr.microsoft.com/dotnet/sdk:8.0 dotnet "$@"
