#!/bin/sh
# Api entrypoint. Migrations belong to the dedicated compose `migrate` service (or a prod job).
# Set RUN_MIGRATIONS=1 only for emergency one-off containers — not the default replica path.
set -eu

RUN_MIGRATIONS="${RUN_MIGRATIONS:-0}"

if [ "$RUN_MIGRATIONS" = "1" ]; then
  if [ -z "${Database__ConnectionString:-}" ]; then
    echo "Database__ConnectionString is required when RUN_MIGRATIONS=1" >&2
    exit 1
  fi
  echo "==> Applying EF migrations (efbundle) — prefer dedicated migrate service"
  /app/efbundle --connection "$Database__ConnectionString"
  echo "==> Migrations applied"
fi

exec dotnet Binexus.Api.dll "$@"
