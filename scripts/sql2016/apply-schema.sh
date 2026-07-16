#!/usr/bin/env bash
# Applies only the generated schema package after a fail-closed preflight.
# It reads ConnectionStrings:Sql2016Target from API User Secrets and never
# accepts or prints a password on the command line.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
api_project="$repo_root/src/backend/ProductionLinePlanner.Api/ProductionLinePlanner.Api.csproj"
schema_script="$repo_root/database/sql2016/001-create-schema.sql"
verify_script="$repo_root/database/sql2016/002-verify-schema.sql"

if [[ "${1:-}" != "--apply" ]]; then
  echo "Refusing to apply. Review the target probe, then rerun: bash scripts/sql2016/apply-schema.sh --apply" >&2
  exit 64
fi

for required in dotnet sqlcmd "$schema_script" "$verify_script"; do
  command -v "$required" >/dev/null 2>&1 || [[ -f "$required" ]] || { echo "Missing required tool or file: $required" >&2; exit 65; }
done

secret_value() {
  local key="$1"
  dotnet user-secrets list --project "$api_project" | sed -n "s/^${key} = //p"
}

connection_value() {
  local connection="$1" key="$2"
  printf '%s' "$connection" | tr ';' '\n' | sed -n "s/^[[:space:]]*${key}[[:space:]]*=[[:space:]]*//Ip" | head -n 1
}

target_connection="$(secret_value 'ConnectionStrings:Sql2016Target')"
source_connection="$(secret_value 'ConnectionStrings:AppDatabase')"
if [[ -z "$target_connection" ]]; then
  echo "ConnectionStrings:Sql2016Target is not configured in API User Secrets." >&2
  exit 66
fi

target_server="$(connection_value "$target_connection" 'Data Source')"
target_database="$(connection_value "$target_connection" 'Initial Catalog')"
target_user="$(connection_value "$target_connection" 'User Id')"
target_password="$(connection_value "$target_connection" 'Password')"
source_server="$(connection_value "$source_connection" 'Data Source')"
source_database="$(connection_value "$source_connection" 'Initial Catalog')"

if [[ -z "$target_server" || -z "$target_database" || -z "$target_user" || -z "$target_password" ]]; then
  echo "Sql2016Target has an unsupported connection-string format." >&2
  unset target_connection target_password
  exit 67
fi

target_identity="$(printf '%s|%s' "$target_server" "$target_database" | tr '[:upper:]' '[:lower:]')"
source_identity="$(printf '%s|%s' "$source_server" "$source_database" | tr '[:upper:]' '[:lower:]')"
if [[ "$target_identity" == "$source_identity" ]]; then
  echo "Refusing to target the configured source application database." >&2
  unset target_connection target_password source_connection target_identity source_identity
  exit 68
fi
unset target_identity source_identity

preflight="SET NOCOUNT ON;
BEGIN TRANSACTION;
DECLARE @TransactionProbePassed bit = 1;
ROLLBACK TRANSACTION;
DECLARE @MigrationHistoryCount int = 0;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
BEGIN
  EXEC sp_executesql N'SELECT @count = COUNT(*) FROM dbo.__EFMigrationsHistory;', N'@count int OUTPUT', @count = @MigrationHistoryCount OUTPUT;
END;
SELECT N'PRECHECK'
  + N'|' + CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(100))
  + N'|' + CAST(SERVERPROPERTY('Edition') AS nvarchar(200))
  + N'|' + DB_NAME()
  + N'|' + SUSER_SNAME()
  + N'|' + CAST((SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0 AND name <> N'__EFMigrationsHistory') AS nvarchar(20))
  + N'|' + CAST(@MigrationHistoryCount AS nvarchar(20))
  + N'|' + CAST(COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE'), 0) AS nvarchar(1))
  + N'|' + CAST(CASE WHEN HAS_PERMS_BY_NAME(N'dbo', 'SCHEMA', 'ALTER') = 1 OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER ANY SCHEMA') = 1 THEN 1 ELSE 0 END AS nvarchar(1))
  + N'|' + CAST(@TransactionProbePassed AS nvarchar(1));"

echo "Target server: $target_server"
echo "Target database: $target_database"
echo "Expected migration count: 17"
echo "Phase 1 applies schema only; no operational production data will be inserted."
preflight_output="$(SQLCMDPASSWORD="$target_password" sqlcmd -C -b -l 30 -S "$target_server" -d "$target_database" -U "$target_user" -W -h -1 -Q "$preflight")"
preflight_line="$(printf '%s\n' "$preflight_output" | grep '^PRECHECK|' || true)"
if [[ -z "$preflight_line" ]]; then
  echo "Target preflight did not return a usable result; refusing to apply." >&2
  unset target_connection target_password source_connection
  exit 69
fi
IFS='|' read -r marker product_version edition database_name login_name user_table_count migration_history_count can_create_table can_alter_dbo_schema transaction_probe_passed <<< "$preflight_line"
echo "Target SQL version: $product_version"
echo "Target edition: $edition"
echo "Target login: $login_name"
echo "Existing application user-table count: $user_table_count"
echo "Existing migration-history row count: $migration_history_count"
echo "Preflight checks (create table / alter dbo schema / transaction probe): $can_create_table / $can_alter_dbo_schema / $transaction_probe_passed"
if [[ "$product_version" != 13.* || "$user_table_count" != 0 || "$migration_history_count" != 0 || "$can_create_table" != 1 || "$can_alter_dbo_schema" != 1 || "$transaction_probe_passed" != 1 ]]; then
  echo "Target preflight is not safe for schema initialization; refusing to apply." >&2
  unset target_connection target_password source_connection
  exit 70
fi

read -r -p "Preflight passed. Type APPLY-SCHEMA to apply schema only: " confirmation
if [[ "$confirmation" != "APPLY-SCHEMA" ]]; then
  echo "Schema apply cancelled before execution."
  unset target_connection target_password source_connection
  exit 0
fi

SQLCMDPASSWORD="$target_password" sqlcmd -C -b -l 60 -S "$target_server" -d "$target_database" -U "$target_user" -i "$schema_script"
SQLCMDPASSWORD="$target_password" sqlcmd -C -b -l 30 -S "$target_server" -d "$target_database" -U "$target_user" -i "$verify_script"
unset target_connection target_password source_connection
