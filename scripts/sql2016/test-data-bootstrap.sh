#!/usr/bin/env bash
# Test Data Bootstrap wrapper. It never accepts connection strings or passwords.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
project="$repo_root/src/backend/ProductionLinePlanner.Tooling/ProductionLinePlanner.Tooling.csproj"

mode="${1:-}"
case "$mode" in
  --preflight)
    command_mode="preflight"
    ;;
  --apply)
    command_mode="apply"
    ;;
  --verify)
    command_mode="verify"
    ;;
  *)
    echo "Usage: scripts/sql2016/test-data-bootstrap.sh --preflight|--apply|--verify" >&2
    exit 64
    ;;
esac

cd "$repo_root"
dotnet run --project "$project" -- test-data "$command_mode"
