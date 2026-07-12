#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
OPENAPI="$ROOT/artifacts/openapi/binexus-v1.json"
SDK_OUT="$ROOT/packages/sdk/src/generated"

if [[ ! -f "$OPENAPI" ]]; then
  echo "Missing $OPENAPI — run: dotnet build apps/backend/src/Binexus.Api/Binexus.Api.csproj"
  exit 1
fi

cd "$ROOT"
pnpm exec openapi-typescript "$OPENAPI" -o "$SDK_OUT/schema.d.ts"
echo "Generated $SDK_OUT/schema.d.ts"