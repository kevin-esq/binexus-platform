#!/usr/bin/env bash
# Gate 5 smoke stack (bash). Prefer PowerShell script on Windows.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
exec pwsh -File "$ROOT/apps/backend/scripts/gate5-smoke-stack.ps1" "$@"
