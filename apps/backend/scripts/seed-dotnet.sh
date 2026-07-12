#!/usr/bin/env bash
# One-shot Identity demo seed against host Postgres (.NET).
# Usage: bash apps/backend/scripts/seed-dotnet.sh [Development|Testing]
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
ENV_NAME="${1:-Development}"
export ASPNETCORE_ENVIRONMENT="$ENV_NAME"
export DOTNET_ENVIRONMENT="$ENV_NAME"
cd "$ROOT/apps/backend"
dotnet run --project src/Binexus.Api/Binexus.Api.csproj --no-launch-profile -- --seed