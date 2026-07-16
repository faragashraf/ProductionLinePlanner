using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace ProductionLinePlanner.Tooling.TestDataBootstrap;

public sealed class TestDataPreflightService(TestDataBootstrapOptions options)
{
    public async Task<PreflightReport> RunAsync(bool writeReport, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var blockers = new List<string>();
        var (sourceConnectionString, targetConnectionString) = LoadConnectionStrings();

        if (SameDatabase(sourceConnectionString, targetConnectionString))
        {
            blockers.Add("SOURCE_TEST_DB and TARGET_SQL2016_DB resolve to the same configured database.");
        }

        await using var source = new SqlConnection(sourceConnectionString);
        await using var target = new SqlConnection(targetConnectionString);
        await source.OpenAsync(cancellationToken);
        await target.OpenAsync(cancellationToken);

        var sourceInfo = await ReadDatabaseInfoAsync(source, cancellationToken);
        var targetInfo = await ReadDatabaseInfoAsync(target, cancellationToken);
        var sourceCounts = await ReadRowCountsAsync(source, cancellationToken);
        var targetCounts = await ReadRowCountsAsync(target, cancellationToken);
        var sourceSchema = await ReadSchemaAsync(source, cancellationToken);
        var targetSchema = await ReadSchemaAsync(target, cancellationToken);
        var sourceSafety = await ReadSafetyAsync(source, cancellationToken);
        var targetSafety = await ReadSafetyAsync(target, cancellationToken);

        if (targetInfo.ProductMajorVersion != 13 || targetInfo.CompatibilityLevel < 130)
        {
            blockers.Add("TARGET_SQL2016_DB is not SQL Server 2016-compatible.");
        }

        if (targetCounts.Count != TestDataMigrationPlan.ExpectedApplicationTableCount)
        {
            blockers.Add($"TARGET_SQL2016_DB has {targetCounts.Count} application tables; expected {TestDataMigrationPlan.ExpectedApplicationTableCount}.");
        }

        if (targetInfo.MigrationHistoryRows != TestDataMigrationPlan.ExpectedMigrationCount)
        {
            blockers.Add($"TARGET_SQL2016_DB has {targetInfo.MigrationHistoryRows} migration rows; expected {TestDataMigrationPlan.ExpectedMigrationCount}.");
        }

        foreach (var table in targetCounts.Where(x => x.Value != 0))
        {
            blockers.Add($"TARGET_SQL2016_DB table {table.Key} is not empty.");
        }

        if (sourceSafety.HasUnsupportedSpecialHandling || targetSafety.HasUnsupportedSpecialHandling)
        {
            blockers.Add("Unsupported app-owned identity/computed/rowversion/trigger/temporal handling was detected.");
        }

        foreach (var plan in TestDataMigrationPlan.Tables)
        {
            var sourceTable = sourceSchema.GetValueOrDefault(plan.FullName);
            var targetTable = targetSchema.GetValueOrDefault(plan.FullName);
            if (sourceTable is null || targetTable is null)
            {
                blockers.Add($"{plan.FullName} is missing from SOURCE_TEST_DB or TARGET_SQL2016_DB.");
                continue;
            }

            if (sourceTable.SchemaFingerprint != targetTable.SchemaFingerprint)
            {
                blockers.Add($"{plan.FullName} schema fingerprint differs between source and target.");
            }
        }

        if (sourceSafety.UntrustedForeignKeys > 0 || sourceSafety.UntrustedCheckConstraints > 0)
        {
            blockers.Add("SOURCE_TEST_DB contains disabled or untrusted FK/check constraints.");
        }

        if (sourceCounts.GetValueOrDefault("dbo.RefreshTokens") > 0)
        {
            warnings.Add("RefreshTokens contains source rows but is excluded by design.");
        }

        if (sourceCounts.GetValueOrDefault("dbo.AuditLogs") > 0)
        {
            warnings.Add("AuditLogs contains source rows but is excluded from the fast initial Test Data Bootstrap.");
        }

        if (sourceCounts.GetValueOrDefault("dbo.AssignmentTimelineEntries") > 0)
        {
            warnings.Add("AssignmentTimelineEntries contains source rows but is excluded because it is not required for FK integrity in this initial migration.");
        }

        var tables = TestDataMigrationPlan.Tables
            .Select(plan =>
            {
                var fullName = plan.FullName;
                var sourceRows = sourceCounts.GetValueOrDefault(fullName);
                var targetRows = targetCounts.GetValueOrDefault(fullName);
                sourceSchema.TryGetValue(fullName, out var sourceTable);
                targetSchema.TryGetValue(fullName, out var targetTable);
                var compatible = sourceTable is not null
                    && targetTable is not null
                    && sourceTable.SchemaFingerprint == targetTable.SchemaFingerprint;
                return new PreflightTableReport(
                    fullName,
                    sourceRows,
                    targetRows,
                    compatible ? "Directly compatible" : "Requires revision",
                    plan.Phase,
                    plan.Include ? "Include" : plan.Decision,
                    plan.KeyStrategy,
                    plan.Notes,
                    sourceTable?.SchemaFingerprint,
                    targetTable?.SchemaFingerprint);
            })
            .OrderBy(x => x.Phase, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Table, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var report = new PreflightReport(
            DateTimeOffset.UtcNow,
            TestDataMigrationPlan.ToolVersion,
            TestDataMigrationPlan.PlanFingerprint,
            await TryReadGitCommitAsync(cancellationToken),
            TestDataMigrationPlan.SourceLabel,
            TestDataMigrationPlan.TargetLabel,
            sourceInfo,
            targetInfo,
            tables,
            sourceSafety,
            targetSafety,
            warnings,
            blockers,
            blockers.Count == 0 ? "Passed" : "Failed");

        if (writeReport)
        {
            await TestDataReportWriter.WriteAsync(options.PreflightReportPath, report, cancellationToken);
        }

        return report;
    }

    private static (string Source, string Target) LoadConnectionStrings()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("src/backend/ProductionLinePlanner.Api/appsettings.json", optional: true)
            .AddJsonFile("src/backend/ProductionLinePlanner.Api/appsettings.Development.json", optional: true)
            .AddUserSecrets<TestDataPreflightService>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var source = configuration.GetConnectionString("AppDatabase");
        var target = configuration.GetConnectionString("Sql2016Target");

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidOperationException("Missing ConnectionStrings:AppDatabase.");
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            throw new InvalidOperationException("Missing ConnectionStrings:Sql2016Target.");
        }

        return (source, target);
    }

    public static (string Source, string Target) LoadConfiguredConnectionStrings() => LoadConnectionStrings();

    private static bool SameDatabase(string source, string target)
    {
        var sourceBuilder = new SqlConnectionStringBuilder(source);
        var targetBuilder = new SqlConnectionStringBuilder(target);
        return string.Equals(sourceBuilder.DataSource, targetBuilder.DataSource, StringComparison.OrdinalIgnoreCase)
            && string.Equals(sourceBuilder.InitialCatalog, targetBuilder.InitialCatalog, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<DatabaseInfo> ReadDatabaseInfoAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SET NOCOUNT ON;
            SELECT
                CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(100)) AS ProductVersion,
                CAST(SERVERPROPERTY('ProductLevel') AS nvarchar(100)) AS ProductLevel,
                CAST(SERVERPROPERTY('Edition') AS nvarchar(200)) AS Edition,
                d.compatibility_level AS CompatibilityLevel,
                (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0 AND name <> N'__EFMigrationsHistory') AS ApplicationTableCount,
                CASE WHEN OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) END AS MigrationHistoryRows
            FROM sys.databases AS d
            WHERE d.name = DB_NAME();
            """;

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Database identity query returned no rows.");
        }

        var productVersion = reader.GetString(0);
        var major = int.TryParse(productVersion.Split('.')[0], out var parsedMajor) ? parsedMajor : 0;
        return new DatabaseInfo(
            productVersion,
            major,
            reader.GetString(1),
            reader.GetString(2),
            Convert.ToInt32(reader.GetValue(3)),
            Convert.ToInt32(reader.GetValue(4)),
            Convert.ToInt32(reader.GetValue(5)));
    }

    private static async Task<Dictionary<string, long>> ReadRowCountsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var tables = await ReadTableNamesAsync(connection, cancellationToken);
        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            await using var command = new SqlCommand($"SELECT COUNT_BIG(*) FROM {QuoteName(table.Schema)}.{QuoteName(table.Table)};", connection);
            var count = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
            counts[$"{table.Schema}.{table.Table}"] = count;
        }

        return counts;
    }

    private static async Task<IReadOnlyList<(string Schema, string Table)>> ReadTableNamesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT s.name, t.name
            FROM sys.tables AS t
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0 AND t.name <> N'__EFMigrationsHistory'
            ORDER BY s.name, t.name;
            """;

        var tables = new List<(string Schema, string Table)>();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add((reader.GetString(0), reader.GetString(1)));
        }

        return tables;
    }

    private static async Task<Dictionary<string, SchemaTableSnapshot>> ReadSchemaAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var parts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        await AppendSchemaRowsAsync(connection, "C", ColumnSql, parts, cancellationToken);
        await AppendSchemaRowsAsync(connection, "I", IndexSql, parts, cancellationToken);
        await AppendSchemaRowsAsync(connection, "F", ForeignKeySql, parts, cancellationToken);
        await AppendSchemaRowsAsync(connection, "K", CheckSql, parts, cancellationToken);

        return parts.ToDictionary(
            x => x.Key,
            x => new SchemaTableSnapshot(x.Key, Sha256(string.Join("\n", x.Value.Order(StringComparer.Ordinal)))),
            StringComparer.OrdinalIgnoreCase);
    }

    private static async Task AppendSchemaRowsAsync(SqlConnection connection, string prefix, string sql, Dictionary<string, List<string>> parts, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var table = reader.GetString(0);
            var value = reader.GetString(1);
            if (!parts.TryGetValue(table, out var tableParts))
            {
                tableParts = [];
                parts[table] = tableParts;
            }

            tableParts.Add($"{prefix}:{value}");
        }
    }

    private static async Task<SafetySnapshot> ReadSafetyAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SET NOCOUNT ON;
            SELECT 'AppIdentityColumns', COUNT(*) FROM sys.columns c INNER JOIN sys.tables t ON t.object_id = c.object_id WHERE t.is_ms_shipped = 0 AND t.name <> N'__EFMigrationsHistory' AND c.is_identity = 1
            UNION ALL SELECT 'AppComputedColumns', COUNT(*) FROM sys.columns c INNER JOIN sys.tables t ON t.object_id = c.object_id WHERE t.is_ms_shipped = 0 AND t.name <> N'__EFMigrationsHistory' AND c.is_computed = 1
            UNION ALL SELECT 'AppRowversionColumns', COUNT(*) FROM sys.columns c INNER JOIN sys.tables t ON t.object_id = c.object_id WHERE t.is_ms_shipped = 0 AND t.name <> N'__EFMigrationsHistory' AND c.system_type_id = 189
            UNION ALL SELECT 'AppTriggers', COUNT(*) FROM sys.triggers tr INNER JOIN sys.tables t ON t.object_id = tr.parent_id WHERE t.is_ms_shipped = 0 AND t.name <> N'__EFMigrationsHistory'
            UNION ALL SELECT 'AppTemporalTables', COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0 AND name <> N'__EFMigrationsHistory' AND temporal_type <> 0
            UNION ALL SELECT 'UntrustedForeignKeys', COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 1 OR is_not_trusted = 1
            UNION ALL SELECT 'UntrustedCheckConstraints', COUNT(*) FROM sys.check_constraints WHERE is_disabled = 1 OR is_not_trusted = 1;
            """;

        var values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values[reader.GetString(0)] = reader.GetInt32(1);
        }

        return new SafetySnapshot(
            values.GetValueOrDefault("AppIdentityColumns"),
            values.GetValueOrDefault("AppComputedColumns"),
            values.GetValueOrDefault("AppRowversionColumns"),
            values.GetValueOrDefault("AppTriggers"),
            values.GetValueOrDefault("AppTemporalTables"),
            values.GetValueOrDefault("UntrustedForeignKeys"),
            values.GetValueOrDefault("UntrustedCheckConstraints"));
    }

    private static async Task<string?> TryReadGitCommitAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("git", "rev-parse HEAD")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string QuoteName(string value) => $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private const string ColumnSql = """
        SELECT s.name + N'.' + t.name AS TableName,
               CONCAT(c.column_id, N':', c.name, N':', ty.name, N':', c.max_length, N':', c.precision, N':', c.scale, N':', c.is_nullable, N':', c.is_identity, N':', c.is_computed, N':', ISNULL(dc.definition, N'')) AS SignaturePart
        FROM sys.columns AS c
        INNER JOIN sys.tables AS t ON t.object_id = c.object_id
        INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
        INNER JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
        LEFT JOIN sys.default_constraints AS dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
        WHERE t.is_ms_shipped = 0 AND t.name <> N'__EFMigrationsHistory'
        ORDER BY s.name, t.name, c.column_id;
        """;

    private const string IndexSql = """
        SELECT s.name + N'.' + t.name AS TableName,
               i.name + N':' + CONVERT(nvarchar(1), i.is_primary_key) + N':' + CONVERT(nvarchar(1), i.is_unique) + N':' + ISNULL(i.filter_definition, N'') + N':' +
               ISNULL((SELECT c.name + N',' FROM sys.index_columns ic INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0 ORDER BY ic.key_ordinal FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), N'') AS SignaturePart
        FROM sys.indexes AS i
        INNER JOIN sys.tables AS t ON t.object_id = i.object_id
        INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
        WHERE t.is_ms_shipped = 0 AND t.name <> N'__EFMigrationsHistory' AND i.is_hypothetical = 0 AND i.type > 0
        ORDER BY s.name, t.name, i.name;
        """;

    private const string ForeignKeySql = """
        SELECT OBJECT_SCHEMA_NAME(fk.parent_object_id) + N'.' + OBJECT_NAME(fk.parent_object_id) AS TableName,
               fk.name + N':' + OBJECT_SCHEMA_NAME(fk.referenced_object_id) + N'.' + OBJECT_NAME(fk.referenced_object_id) + N':' + CONVERT(nvarchar(10), fk.delete_referential_action) + N':' +
               ISNULL((SELECT pc.name + N'>' + rc.name + N',' FROM sys.foreign_key_columns fkc INNER JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id INNER JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id WHERE fkc.constraint_object_id = fk.object_id ORDER BY fkc.constraint_column_id FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), N'') AS SignaturePart
        FROM sys.foreign_keys AS fk
        WHERE fk.is_ms_shipped = 0
        ORDER BY TableName, fk.name;
        """;

    private const string CheckSql = """
        SELECT OBJECT_SCHEMA_NAME(cc.parent_object_id) + N'.' + OBJECT_NAME(cc.parent_object_id) AS TableName,
               cc.name + N':' + cc.definition AS SignaturePart
        FROM sys.check_constraints AS cc
        ORDER BY TableName, cc.name;
        """;
}

public sealed record DatabaseInfo(
    string ProductVersion,
    int ProductMajorVersion,
    string ProductLevel,
    string Edition,
    int CompatibilityLevel,
    int ApplicationTableCount,
    int MigrationHistoryRows);

public sealed record SafetySnapshot(
    int AppIdentityColumns,
    int AppComputedColumns,
    int AppRowversionColumns,
    int AppTriggers,
    int AppTemporalTables,
    int UntrustedForeignKeys,
    int UntrustedCheckConstraints)
{
    public bool HasUnsupportedSpecialHandling =>
        AppIdentityColumns > 0 || AppComputedColumns > 0 || AppRowversionColumns > 0 || AppTriggers > 0 || AppTemporalTables > 0;
}

public sealed record SchemaTableSnapshot(string Table, string SchemaFingerprint);

public sealed record PreflightTableReport(
    string Table,
    long SourceRows,
    long TargetRows,
    string Compatibility,
    string Phase,
    string Decision,
    string KeyStrategy,
    string Notes,
    string? SourceSchemaFingerprint,
    string? TargetSchemaFingerprint);

public sealed record PreflightReport(
    DateTimeOffset TimestampUtc,
    string ToolVersion,
    string PlanFingerprint,
    string? GitCommit,
    string SourceLabel,
    string TargetLabel,
    DatabaseInfo Source,
    DatabaseInfo Target,
    IReadOnlyList<PreflightTableReport> Tables,
    SafetySnapshot SourceSafety,
    SafetySnapshot TargetSafety,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Blockers,
    string OverallResult);
