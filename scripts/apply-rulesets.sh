#!/usr/bin/env bash
# Apply / list / delete GitHub Modern Rulesets from .github/rulesets/.
#
# Usage:
#   scripts/apply-rulesets.sh apply
#   scripts/apply-rulesets.sh list
#   scripts/apply-rulesets.sh delete <name>
#
# Requires:
#   - gh authenticated with admin on the repo
#   - jq
set -euo pipefail

ACTION="${1:-}"
NAME="${2:-}"

if [[ -z "$ACTION" ]]; then
  echo "usage: $0 apply | list | delete <name>" >&2
  exit 2
fi

command -v gh >/dev/null || { echo "gh CLI not found" >&2; exit 127; }
command -v jq >/dev/null || { echo "jq not found"      >&2; exit 127; }

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RULESETS_DIR="$SCRIPT_DIR/../.github/rulesets"

REMOTE_URL="$(git config --get remote.origin.url || true)"
[[ -n "$REMOTE_URL" ]] || { echo "no origin remote" >&2; exit 1; }
REPO="$(echo "$REMOTE_URL" | sed -E 's#.*github\.com[:/]([^/]+)/([^/.]+)(\.git)?#\1/\2#')"
echo "Repo: $REPO"

list_rulesets() {
  gh api "repos/$REPO/rulesets"
}

find_id_by_name() {
  local name="$1"
  list_rulesets | jq -r --arg n "$name" '.[] | select(.name == $n) | .id'
}

apply_one() {
  local file="$1"
  local name
  name="$(jq -r .name "$file")"
  [[ -n "$name" && "$name" != "null" ]] || { echo "no name in $file" >&2; exit 1; }

  local id
  id="$(find_id_by_name "$name" || true)"
  if [[ -n "$id" ]]; then
    echo "Updating ruleset '$name' (id=$id)..."
    gh api -X PUT "repos/$REPO/rulesets/$id" \
      -H "Accept: application/vnd.github+json" \
      -H "X-GitHub-Api-Version: 2022-11-28" \
      --input "$file" >/dev/null
  else
    echo "Creating ruleset '$name'..."
    gh api -X POST "repos/$REPO/rulesets" \
      -H "Accept: application/vnd.github+json" \
      -H "X-GitHub-Api-Version: 2022-11-28" \
      --input "$file" >/dev/null
  fi
  echo "  OK"
}

case "$ACTION" in
  apply)
    shopt -s nullglob
    files=("$RULESETS_DIR"/*.json)
    [[ ${#files[@]} -gt 0 ]] || { echo "no *.json in $RULESETS_DIR" >&2; exit 1; }
    for f in "${files[@]}"; do
      apply_one "$f"
    done
    echo
    echo "Current rulesets:"
    list_rulesets | jq -r '.[] | "\(.id)\t\(.name)\t\(.target)\t\(.enforcement)"'
    ;;
  list)
    list_rulesets | jq -r '.[] | "\(.id)\t\(.name)\t\(.target)\t\(.enforcement)"'
    ;;
  delete)
    [[ -n "$NAME" ]] || { echo "name required" >&2; exit 2; }
    id="$(find_id_by_name "$NAME" || true)"
    if [[ -z "$id" ]]; then
      echo "Ruleset '$NAME' does not exist."
      exit 0
    fi
    echo "Deleting ruleset '$NAME' (id=$id)..."
    gh api -X DELETE "repos/$REPO/rulesets/$id" \
      -H "Accept: application/vnd.github+json" \
      -H "X-GitHub-Api-Version: 2022-11-28" >/dev/null
    echo "  OK"
    ;;
  *)
    echo "unknown action: $ACTION" >&2
    exit 2
    ;;
esac
