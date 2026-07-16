#!/usr/bin/env bash
# Read-only SQL Server target probe. It prints no connection-string values.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
api_project="$repo_root/src/backend/ProductionLinePlanner.Api/ProductionLinePlanner.Api.csproj"

secret_value() {
  local key="$1"
  dotnet user-secrets list --project "$api_project" | sed -n "s/^${key} = //p"
}

connection_value() {
  local connection="$1" key="$2"
  printf '%s' "$connection" | tr ';' '\n' | sed -n "s/^[[:space:]]*${key}[[:space:]]*=[[:space:]]*//Ip" | head -n 1
}

target_connection="$(secret_value 'ConnectionStrings:Sql2016Target')"
if [[ -z "$target_connection" ]]; then
  echo "ConnectionStrings:Sql2016Target is not configured in API User Secrets." >&2
  exit 66
fi

target_server="$(connection_value "$target_connection" 'Data Source')"
target_database="$(connection_value "$target_connection" 'Initial Catalog')"
target_user="$(connection_value "$target_connection" 'User Id')"
target_password="$(connection_value "$target_connection" 'Password')"
if [[ -z "$target_server" || -z "$target_database" || -z "$target_user" || -z "$target_password" ]]; then
  echo "Sql2016Target has an unsupported connection-string format." >&2
  unset target_connection target_password
  exit 67
fi

probe_sql="SET NOCOUNT ON;
SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(100)) AS ProductVersion, CAST(SERVERPROPERTY('ProductLevel') AS nvarchar(100)) AS ProductLevel, CAST(SERVERPROPERTY('Edition') AS nvarchar(200)) AS Edition, DB_NAME() AS DatabaseName, SUSER_SNAME() AS LoginName;
SELECT compatibility_level AS CompatibilityLevel FROM sys.databases WHERE name = DB_NAME();
SELECT
  HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE') AS CanCreateTable,
  CASE WHEN HAS_PERMS_BY_NAME(N'dbo', 'SCHEMA', 'ALTER') = 1 OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER ANY SCHEMA') = 1 THEN 1 ELSE 0 END AS CanAlterDboSchema;
BEGIN TRANSACTION; SELECT CAST(1 AS bit) AS TransactionProbePassed; ROLLBACK TRANSACTION;
SELECT COUNT(*) AS UserTableCount FROM sys.tables WHERE is_ms_shipped = 0;
SELECT CASE WHEN OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS MigrationHistoryExists;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
BEGIN
  SELECT COUNT(*) AS MigrationHistoryRowCount FROM dbo.__EFMigrationsHistory;
  SELECT MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
END
ELSE
  SELECT CAST(0 AS int) AS MigrationHistoryRowCount;
SELECT s.name AS SchemaName, t.name AS TableName, SUM(p.rows) AS ApproximateRows
FROM sys.tables AS t
INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
LEFT JOIN sys.partitions AS p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE t.is_ms_shipped = 0
GROUP BY s.name, t.name
ORDER BY s.name, t.name;
EXEC sp_spaceused @oneresultset = 1;"

echo "Read-only target probe: server=$target_server database=$target_database"
SQLCMDPASSWORD="$target_password" sqlcmd -C -b -l 30 -S "$target_server" -d "$target_database" -U "$target_user" -W -s '|' -Q "$probe_sql"
unset target_connection target_password
