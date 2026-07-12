#!/usr/bin/env bash
# Gate 6/7 compose smoke — isolated COMPOSE_PROJECT_NAME + ports. Never kills host processes.
# Usage (repo root):
#   export Jwt__SigningKey='local-build-signing-key-with-more-than-thirty-two-bytes'
#   bash apps/backend/scripts/gate6-compose-smoke.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
COMPOSE_FILE="$ROOT/infrastructure/compose/docker-compose.yml"
SUFFIX="${SMOKE_ID:-$$}"
export COMPOSE_PROJECT_NAME="${COMPOSE_PROJECT_NAME:-binexus-smoke-${SUFFIX}}"

# Isolated host ports (avoid clashing with a developer's :5102 / :5103 / :9000).
API_SMOKE_PORT="${API_SMOKE_PORT:-5112}"
WORKERS_SMOKE_PORT="${WORKERS_SMOKE_PORT:-5113}"
MINIO_SMOKE_PORT="${MINIO_SMOKE_PORT:-9100}"
MINIO_CONSOLE_SMOKE_PORT="${MINIO_CONSOLE_SMOKE_PORT:-9101}"
POSTGRES_SMOKE_PORT="${POSTGRES_SMOKE_PORT:-55432}"

export BINEXUS_API_HOST_PORT="$API_SMOKE_PORT"
export BINEXUS_WORKERS_HOST_PORT="$WORKERS_SMOKE_PORT"
export BINEXUS_MINIO_HOST_PORT="$MINIO_SMOKE_PORT"
export BINEXUS_MINIO_CONSOLE_HOST_PORT="$MINIO_CONSOLE_SMOKE_PORT"
export BINEXUS_POSTGRES_HOST_PORT="$POSTGRES_SMOKE_PORT"

API_URL="${SMOKE_API_URL:-http://localhost:${API_SMOKE_PORT}}"
WORKERS_URL="${SMOKE_WORKERS_URL:-http://localhost:${WORKERS_SMOKE_PORT}}"
TIMEOUT_SEC="${HEALTH_TIMEOUT_SEC:-180}"
LOG_DIR="${SMOKE_LOG_DIR:-$ROOT/artifacts/gate6-smoke}"

port_in_use() {
  local port="$1"
  if command -v ss >/dev/null 2>&1; then
    ss -tln 2>/dev/null | grep -qE ":${port}\\b" && return 0
  fi
  if command -v lsof >/dev/null 2>&1; then
    lsof -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1 && return 0
  fi
  return 1
}

abort_if_port_busy() {
  local port="$1"
  local label="$2"
  if port_in_use "$port"; then
    echo "SMOKE FAIL: host port ${port} (${label}) is already in use." >&2
    echo "  Smoke uses isolated ports and never kills host processes." >&2
    echo "  Free the port, or set ${label} to another free port and re-run." >&2
    if command -v ss >/dev/null 2>&1; then
      ss -tlnp 2>/dev/null | grep -E ":${port}\\b" || true
    fi
    exit 1
  fi
}

abort_if_port_busy "$API_SMOKE_PORT" "API_SMOKE_PORT"
abort_if_port_busy "$WORKERS_SMOKE_PORT" "WORKERS_SMOKE_PORT"
abort_if_port_busy "$MINIO_SMOKE_PORT" "MINIO_SMOKE_PORT"
abort_if_port_busy "$POSTGRES_SMOKE_PORT" "POSTGRES_SMOKE_PORT"

if [[ -z "${Jwt__SigningKey:-}" ]]; then
  export Jwt__SigningKey='local-build-signing-key-with-more-than-thirty-two-bytes'
  echo "==> Jwt__SigningKey not set; using DEVELOPMENT-ONLY local default"
fi

export IdentitySeed__AdminPassword="${IdentitySeed__AdminPassword:-ChangeMe123!}"
export Logistics__Storage__Provider=MinIO
export Logistics__Storage__Endpoint=http://minio:9000
export Logistics__Storage__InternalEndpoint=http://minio:9000
export Logistics__Storage__PublicEndpoint="http://localhost:${MINIO_SMOKE_PORT}"
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
export DOTNET_ENVIRONMENT="${DOTNET_ENVIRONMENT:-Development}"
export RUN_MIGRATIONS=0

cd "$ROOT"
mkdir -p "$LOG_DIR"

echo "==> COMPOSE_PROJECT_NAME=$COMPOSE_PROJECT_NAME"
echo "==> ports api=$API_SMOKE_PORT workers=$WORKERS_SMOKE_PORT minio=$MINIO_SMOKE_PORT postgres=$POSTGRES_SMOKE_PORT"

echo "==> docker compose build + up (project=$COMPOSE_PROJECT_NAME)"
docker compose -f "$COMPOSE_FILE" -p "$COMPOSE_PROJECT_NAME" build api workers
docker compose -f "$COMPOSE_FILE" -p "$COMPOSE_PROJECT_NAME" up -d postgres minio minio-bucket-init migrate api workers

cleanup() {
  local code=$?
  if [[ $code -ne 0 ]]; then
    echo "==> collecting logs to $LOG_DIR" >&2
    docker compose -f "$COMPOSE_FILE" -p "$COMPOSE_PROJECT_NAME" ps -a >"$LOG_DIR/compose-ps.txt" 2>&1 || true
    docker compose -f "$COMPOSE_FILE" -p "$COMPOSE_PROJECT_NAME" logs --no-color --tail=200 api >"$LOG_DIR/api.log" 2>&1 || true
    docker compose -f "$COMPOSE_FILE" -p "$COMPOSE_PROJECT_NAME" logs --no-color --tail=200 workers >"$LOG_DIR/workers.log" 2>&1 || true
    docker compose -f "$COMPOSE_FILE" -p "$COMPOSE_PROJECT_NAME" logs --no-color --tail=80 migrate >"$LOG_DIR/migrate.log" 2>&1 || true
  fi
  if [[ "${KEEP_RUNNING:-0}" != "1" ]]; then
    echo "==> docker compose down -p $COMPOSE_PROJECT_NAME (containers owned by this smoke only)"
    docker compose -f "$COMPOSE_FILE" -p "$COMPOSE_PROJECT_NAME" --profile web --profile seed down --remove-orphans || true
  fi
}
trap cleanup EXIT

wait_http() {
  local url="$1"
  local label="$2"
  echo "==> wait for $url (${TIMEOUT_SEC}s) [$label]"
  local deadline=$((SECONDS + TIMEOUT_SEC))
  until curl -fsS "$url" >/dev/null 2>&1; do
    if (( SECONDS >= deadline )); then
      echo "$label not ready; logs:" >&2
      docker compose -f "$COMPOSE_FILE" -p "$COMPOSE_PROJECT_NAME" logs --tail=80 api workers migrate >&2 || true
      exit 1
    fi
    sleep 2
  done
  echo "$label ready"
}

wait_http "$API_URL/health/ready" "API"
wait_http "$WORKERS_URL/health" "Workers"

services="$(docker compose -f "$COMPOSE_FILE" -p "$COMPOSE_PROJECT_NAME" ps --services 2>/dev/null || true)"
if echo "$services" | grep -qx 'redis'; then
  echo "Unexpected redis service in default stack: $services" >&2
  exit 1
fi

echo "==> re-run migrator (idempotent)"
docker compose -f "$COMPOSE_FILE" -p "$COMPOSE_PROJECT_NAME" run --rm migrate

echo "==> SMOKE_REQUIRE=1 (MinIO public http://localhost:${MINIO_SMOKE_PORT})"
export SMOKE_REQUIRE=1
export SMOKE_EXPECT_MINIO=1
export SMOKE_API_URL="$API_URL"
export NEXT_PUBLIC_API_URL="$API_URL"
node "$ROOT/apps/web/scripts/smoke-dotnet.mjs" | tee "$LOG_DIR/smoke.log"

echo "GATE6 COMPOSE SMOKE PASS"