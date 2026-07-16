using Microsoft.Data.SqlClient;
using ProductionLinePlanner.Domain.Authorization;
using ProductionLinePlanner.Infrastructure.Authorization;

namespace ProductionLinePlanner.Tooling.TestDataBootstrap;

public sealed class TestDataVerificationService(TestDataBootstrapOptions options)
{
    public async Task<int> VerifyAsync(CancellationToken cancellationToken)
    {
        var preflight = await TestDataReportWriter.ReadAsync<PreflightReport>(options.PreflightReportPath, cancellationToken);
        if (preflight is null || preflight.OverallResult != "Passed")
        {
            Console.Error.WriteLine("A successful preflight report is required before verify.");
            return 2;
        }

        var (_, targetConnectionString) = TestDataPreflightService.LoadConfiguredConnectionStrings();
        await using var target = new SqlConnection(targetConnectionString);
        await target.OpenAsync(cancellationToken);

        var targetCounts = await ReadTargetCountsAsync(target, cancellationToken);
        var blockers = new List<string>();
        var warnings = new List<string>();

        foreach (var table in preflight.Tables.Where(x => x.Decision == "Include"))
        {
            var actual = targetCounts.GetValueOrDefault(table.Table);
            if (actual != table.SourceRows)
            {
                blockers.Add($"{table.Table} target rows {actual} do not match planned source rows {table.SourceRows}.");
            }
        }

        foreach (var excluded in new[] { "dbo.RefreshTokens", "dbo.AuditLogs", "dbo.AssignmentTimelineEntries", "dbo.UserPermissionOverrides" })
        {
            if (targetCounts.GetValueOrDefault(excluded) != 0)
            {
                blockers.Add($"{excluded} must remain empty after Test Data Bootstrap.");
            }
        }

        var migrationRows = await ExecuteScalarIntAsync(target, "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory;", cancellationToken);
        if (migrationRows != TestDataMigrationPlan.ExpectedMigrationCount)
        {
            blockers.Add("TARGET_SQL2016_DB migration history row count changed unexpectedly.");
        }

        var orphanCount = await ExecuteScalarLongAsync(target, OrphanSql, cancellationToken);
        if (orphanCount != 0)
        {
            blockers.Add($"TARGET_SQL2016_DB has {orphanCount} orphan rows after import.");
        }

        await VerifyIamBaselineAsync(target, blockers, cancellationToken);
        await VerifyFocusedUniqueKeysAsync(target, blockers, cancellationToken);

        var report = new VerificationReport(
            DateTimeOffset.UtcNow,
            TestDataMigrationPlan.ToolVersion,
            TestDataMigrationPlan.PlanFingerprint,
            TestDataMigrationPlan.TargetLabel,
            targetCounts.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value),
            new VerificationAggregates(
                targetCounts.GetValueOrDefault("dbo.Workers"),
                await ExecuteScalarLongAsync(target, "SELECT COUNT_BIG(*) FROM dbo.WorkerDefaultAssignments WHERE IsActive = 1;", cancellationToken),
                targetCounts.GetValueOrDefault("dbo.AttendanceRecords"),
                targetCounts.GetValueOrDefault("dbo.ProductionOrders"),
                targetCounts.GetValueOrDefault("dbo.StageProductionRecords"),
                targetCounts.GetValueOrDefault("dbo.StageProductionWorkerAllocations"),
                targetCounts.GetValueOrDefault("dbo.WorkerSalaryHistories")),
            warnings,
            blockers,
            blockers.Count == 0 ? "Passed" : "Failed");

        await TestDataReportWriter.WriteAsync(options.VerificationReportPath, report, cancellationToken);
        Console.WriteLine($"Verification result: {report.OverallResult}");
        Console.WriteLine($"Sanitized report: {Path.GetRelativePath(options.RepoRoot, options.VerificationReportPath)}");
        return blockers.Count == 0 ? 0 : 2;
    }

    private static async Task<Dictionary<string, long>> ReadTargetCountsAsync(SqlConnection target, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        const string tableSql = """
            SELECT s.name, t.name
            FROM sys.tables AS t
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0 AND t.name <> N'__EFMigrationsHistory'
            ORDER BY s.name, t.name;
            """;

        var tables = new List<(string Schema, string Table)>();
        await using (var command = new SqlCommand(tableSql, target))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        foreach (var table in tables)
        {
            await using var command = new SqlCommand($"SELECT COUNT_BIG(*) FROM {QuoteName(table.Schema)}.{QuoteName(table.Table)};", target);
            counts[$"{table.Schema}.{table.Table}"] = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }

        return counts;
    }

    private static async Task<int> ExecuteScalarIntAsync(SqlConnection target, string sql, CancellationToken cancellationToken) =>
        Convert.ToInt32(await new SqlCommand(sql, target).ExecuteScalarAsync(cancellationToken));

    private static async Task<long> ExecuteScalarLongAsync(SqlConnection target, string sql, CancellationToken cancellationToken) =>
        Convert.ToInt64(await new SqlCommand(sql, target).ExecuteScalarAsync(cancellationToken));

    private static async Task<long> ExecuteScalarLongAsync(SqlConnection target, string sql, IReadOnlyDictionary<string, object> parameters, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, target);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Key, parameter.Value);
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task VerifyIamBaselineAsync(SqlConnection target, List<string> blockers, CancellationToken cancellationToken)
    {
        foreach (var role in SystemRoleCatalog.All)
        {
            var roleValue = role.ToString();
            var roleCount = await ExecuteScalarLongAsync(
                target,
                "SELECT COUNT_BIG(*) FROM dbo.AppRoles WHERE Role = @role AND Name = @role AND IsSystemRole = 1 AND IsActive = 1;",
                new Dictionary<string, object> { ["@role"] = roleValue },
                cancellationToken);
            if (roleCount != 1)
            {
                blockers.Add("TARGET_SQL2016_DB is missing a required active system role.");
            }

            foreach (var permissionName in PermissionSeedService.GetPermissionsForRole(role).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var grantCount = await ExecuteScalarLongAsync(
                    target,
                    """
                    SELECT COUNT_BIG(*)
                    FROM dbo.AppRoles AS ar
                    INNER JOIN dbo.RolePermissions AS rp ON rp.AppRoleId = ar.Id
                    INNER JOIN dbo.Permissions AS p ON p.Id = rp.PermissionId
                    WHERE ar.Role = @role AND p.Name = @permissionName AND p.IsActive = 1;
                    """,
                    new Dictionary<string, object>
                    {
                        ["@role"] = roleValue,
                        ["@permissionName"] = permissionName
                    },
                    cancellationToken);
                if (grantCount != 1)
                {
                    blockers.Add("TARGET_SQL2016_DB is missing a required system role permission grant.");
                }
            }
        }

        foreach (var permission in PermissionCatalog.All)
        {
            var permissionCount = await ExecuteScalarLongAsync(
                target,
                "SELECT COUNT_BIG(*) FROM dbo.Permissions WHERE Name = @name AND Capability = @capability AND IsActive = 1;",
                new Dictionary<string, object>
                {
                    ["@name"] = permission.Name,
                    ["@capability"] = permission.Capability
                },
                cancellationToken);
            if (permissionCount != 1)
            {
                blockers.Add("TARGET_SQL2016_DB is missing a required active permission catalog entry.");
            }
        }

        var orphanUserRoles = await ExecuteScalarLongAsync(
            target,
            """
            SELECT COUNT_BIG(*)
            FROM dbo.UserRoles AS ur
            LEFT JOIN dbo.AppUsers AS u ON u.Id = ur.AppUserId
            LEFT JOIN dbo.AppRoles AS ar ON ar.Id = ur.AppRoleId
            WHERE u.Id IS NULL OR ar.Id IS NULL;
            """,
            cancellationToken);
        if (orphanUserRoles != 0)
        {
            blockers.Add("TARGET_SQL2016_DB has user-role assignments without matching users or roles.");
        }

        var usersWithoutRoles = await ExecuteScalarLongAsync(
            target,
            """
            SELECT COUNT_BIG(*)
            FROM dbo.AppUsers AS u
            WHERE NOT EXISTS (SELECT 1 FROM dbo.UserRoles AS ur WHERE ur.AppUserId = u.Id);
            """,
            cancellationToken);
        if (usersWithoutRoles != 0)
        {
            blockers.Add("TARGET_SQL2016_DB has migrated users without a role assignment.");
        }
    }

    private static async Task VerifyFocusedUniqueKeysAsync(SqlConnection target, List<string> blockers, CancellationToken cancellationToken)
    {
        var duplicateCount = await ExecuteScalarLongAsync(target, FocusedDuplicateSql, cancellationToken);
        if (duplicateCount != 0)
        {
            blockers.Add("TARGET_SQL2016_DB has duplicate values for one or more focused natural-key checks.");
        }
    }

    private static string QuoteName(string value) => $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";

    private const string FocusedDuplicateSql = """
        SELECT
            (SELECT COUNT_BIG(*) FROM (SELECT Role FROM dbo.AppRoles WHERE Role IS NOT NULL GROUP BY Role HAVING COUNT_BIG(*) > 1) AS d)
          + (SELECT COUNT_BIG(*) FROM (SELECT Name FROM dbo.AppRoles GROUP BY Name HAVING COUNT_BIG(*) > 1) AS d)
          + (SELECT COUNT_BIG(*) FROM (SELECT Name FROM dbo.Permissions GROUP BY Name HAVING COUNT_BIG(*) > 1) AS d)
          + (SELECT COUNT_BIG(*) FROM (SELECT Code FROM dbo.Factories GROUP BY Code HAVING COUNT_BIG(*) > 1) AS d)
          + (SELECT COUNT_BIG(*) FROM (SELECT FactoryId, LineCode FROM dbo.ProductionLines WHERE LineCode IS NOT NULL GROUP BY FactoryId, LineCode HAVING COUNT_BIG(*) > 1) AS d)
          + (SELECT COUNT_BIG(*) FROM (SELECT Code FROM dbo.SubStages GROUP BY Code HAVING COUNT_BIG(*) > 1) AS d)
          + (SELECT COUNT_BIG(*) FROM (SELECT Code FROM dbo.ProductModels GROUP BY Code HAVING COUNT_BIG(*) > 1) AS d)
          + (SELECT COUNT_BIG(*) FROM (SELECT EmployeeCode FROM dbo.Workers GROUP BY EmployeeCode HAVING COUNT_BIG(*) > 1) AS d)
          + (SELECT COUNT_BIG(*) FROM (SELECT OrderNumber FROM dbo.ProductionOrders GROUP BY OrderNumber HAVING COUNT_BIG(*) > 1) AS d);
        """;

    private const string OrphanSql = """
        SELECT
            (SELECT COUNT_BIG(*) FROM dbo.ProductionLines AS c LEFT JOIN dbo.Factories AS p ON p.Id = c.FactoryId WHERE p.Id IS NULL)
          + (SELECT COUNT_BIG(*) FROM dbo.MainStages AS c LEFT JOIN dbo.ProductionLines AS p ON p.Id = c.ProductionLineId WHERE p.Id IS NULL)
          + (SELECT COUNT_BIG(*) FROM dbo.SubStages AS c LEFT JOIN dbo.MainStages AS p ON p.Id = c.MainStageId WHERE p.Id IS NULL)
          + (SELECT COUNT_BIG(*) FROM dbo.ProductModelStages AS c LEFT JOIN dbo.ProductModels AS p ON p.Id = c.ProductModelId WHERE p.Id IS NULL)
          + (SELECT COUNT_BIG(*) FROM dbo.ProductModelStages AS c LEFT JOIN dbo.SubStages AS p ON p.Id = c.SubStageId WHERE p.Id IS NULL)
          + (SELECT COUNT_BIG(*) FROM dbo.WorkerDefaultAssignments AS c LEFT JOIN dbo.Workers AS p ON p.Id = c.WorkerId WHERE p.Id IS NULL)
          + (SELECT COUNT_BIG(*) FROM dbo.WorkerDefaultAssignments AS c LEFT JOIN dbo.SubStages AS p ON p.Id = c.SubStageId WHERE p.Id IS NULL)
          + (SELECT COUNT_BIG(*) FROM dbo.WorkerTemporaryAssignments AS c LEFT JOIN dbo.Workers AS p ON p.Id = c.WorkerId WHERE p.Id IS NULL)
          + (SELECT COUNT_BIG(*) FROM dbo.AttendanceRecords AS c LEFT JOIN dbo.Workers AS p ON p.Id = c.WorkerId WHERE p.Id IS NULL)
          + (SELECT COUNT_BIG(*) FROM dbo.ProductionOrders AS c LEFT JOIN dbo.ProductModels AS p ON p.Id = c.ProductModelId WHERE p.Id IS NULL)
          + (SELECT COUNT_BIG(*) FROM dbo.ProductionOrders AS c LEFT JOIN dbo.ProductionLines AS p ON p.Id = c.ProductionLineId WHERE c.ProductionLineId IS NOT NULL AND p.Id IS NULL)
          + (SELECT COUNT_BIG(*) FROM dbo.StageProductionRecords AS c LEFT JOIN dbo.ProductionOrders AS p ON p.Id = c.ProductionOrderId WHERE p.Id IS NULL)
          + (SELECT COUNT_BIG(*) FROM dbo.StageProductionRecords AS c LEFT JOIN dbo.ProductModelStages AS p ON p.Id = c.ProductModelStageId WHERE p.Id IS NULL)
          + (SELECT COUNT_BIG(*) FROM dbo.StageProductionWorkerAllocations AS c LEFT JOIN dbo.StageProductionRecords AS p ON p.Id = c.StageProductionRecordId WHERE p.Id IS NULL)
          + (SELECT COUNT_BIG(*) FROM dbo.StageProductionWorkerAllocations AS c LEFT JOIN dbo.Workers AS p ON p.Id = c.WorkerId WHERE p.Id IS NULL);
        """;
}

public sealed record VerificationAggregates(
    long Workers,
    long ActiveDefaultAssignments,
    long AttendanceRecords,
    long ProductionOrders,
    long StageProductionRecords,
    long WorkerAllocations,
    long SalaryHistories);

public sealed record VerificationReport(
    DateTimeOffset TimestampUtc,
    string ToolVersion,
    string PlanFingerprint,
    string TargetLabel,
    IReadOnlyDictionary<string, long> TargetRows,
    VerificationAggregates Aggregates,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Blockers,
    string OverallResult);
