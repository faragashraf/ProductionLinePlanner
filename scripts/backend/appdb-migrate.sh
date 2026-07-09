#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BACKEND_DIR="$ROOT_DIR/src/backend"
API_PROJECT="$BACKEND_DIR/ProductionLinePlanner.Api/ProductionLinePlanner.Api.csproj"
INFRA_PROJECT="$BACKEND_DIR/ProductionLinePlanner.Infrastructure/ProductionLinePlanner.Infrastructure.csproj"

echo "==> Restoring .NET tools..."
dotnet tool restore

APP_DB_CONN_FROM_ENV="${ConnectionStrings__AppDatabase:-}"
APP_DB_CONN_FROM_SECRET=""

USER_SECRET_OUTPUT="$(dotnet user-secrets list --project "$API_PROJECT" 2>/dev/null || true)"
APP_DB_CONN_FROM_SECRET="$(printf '%s\n' "$USER_SECRET_OUTPUT" | awk -F ' = ' '$1 == "ConnectionStrings:AppDatabase" { print $2; exit }')"

APP_DB_CONNECTION=""
APP_DB_SOURCE=""

if [ -n "$APP_DB_CONN_FROM_ENV" ]; then
  APP_DB_CONNECTION="$APP_DB_CONN_FROM_ENV"
  APP_DB_SOURCE="environment variable (ConnectionStrings__AppDatabase)"
elif [ -n "$APP_DB_CONN_FROM_SECRET" ]; then
  APP_DB_CONNECTION="$APP_DB_CONN_FROM_SECRET"
  APP_DB_SOURCE="user-secrets (ConnectionStrings:AppDatabase)"
fi

if [ -z "$APP_DB_CONNECTION" ]; then
  echo "ERROR: no ConnectionStrings:AppDatabase found."
  echo "Set it first using one of:"
  echo "  1) dotnet user-secrets set \"ConnectionStrings:AppDatabase\" \"...\" --project \"$API_PROJECT\""
  echo "  2) export ConnectionStrings__AppDatabase=\"...\""
  exit 1
fi

if [[ "$APP_DB_CONNECTION" == *"REPLACE_WITH_USER_SECRET"* ]] || [[ "$APP_DB_CONNECTION" == *"<real-app-db-connection-string>"* ]]; then
  echo "ERROR: ConnectionStrings:AppDatabase still contains placeholder value."
  exit 1
fi

echo "==> Applying migrations for FactoryPlannerDB using AppDbContext ..."
dotnet tool run dotnet-ef database update \
  --project "$INFRA_PROJECT" \
  --startup-project "$API_PROJECT" \
  --context AppDbContext

echo "==> Done. Migrated using $APP_DB_SOURCE."
