#!/usr/bin/env bash
set -euo pipefail
ENDPOINT="${ENDPOINT:-http://127.0.0.1:9000}"
ACCESS_KEY="${ACCESS_KEY:-binexus}"
SECRET_KEY="${SECRET_KEY:-binexus12345}"
BUCKET="${BUCKET:-binexus-proofs}"
ROOT="$(cd "$(dirname "$0")" && pwd)"

mc alias set binexuslocal "$ENDPOINT" "$ACCESS_KEY" "$SECRET_KEY"
mc mb --ignore-existing "binexuslocal/$BUCKET"
mc anonymous set none "binexuslocal/$BUCKET"
mc cors set "binexuslocal/$BUCKET" "$ROOT/cors.json"
echo "Bucket $BUCKET ready with CORS for localhost:3000"
