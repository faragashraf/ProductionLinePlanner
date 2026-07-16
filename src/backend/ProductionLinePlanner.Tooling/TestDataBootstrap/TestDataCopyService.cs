using Microsoft.Data.SqlClient;
using ProductionLinePlanner.Domain.Authorization;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Authorization;

namespace ProductionLinePlanner.Tooling.TestDataBootstrap;

public sealed class TestDataCopyService(TestDataBootstrapOptions options)
{
    public async Task<int> ApplyAsync(CancellationToken cancellationToken)
    {
        var report = await TestDataReportWriter.ReadAsync<PreflightReport>(options.PreflightReportPath, cancellationToken);
        if (report is null || report.OverallResult != "Passed")
        {
            Console.Error.WriteLine("A successful preflight report is required before apply.");
            return 2;
        }

        if (DateTimeOffset.UtcNow - report.TimestampUtc > TimeSpan.FromMinutes(30))
        {
            Console.Error.WriteLine("The preflight report is stale; rerun preflight before apply.");
            return 2;
        }

        var fresh = await new TestDataPreflightService(options).RunAsync(writeReport: false, cancellationToken);
        if (fresh.OverallResult != "Passed" || !MatchesReport(report, fresh))
        {
            Console.Error.WriteLine("Current source/target state no longer matches the preflight report.");
            return 2;
        }

        var (sourceConnectionString, targetConnectionString) = TestDataPreflightService.LoadConfiguredConnectionStrings();
        await using var source = new SqlConnection(sourceConnectionString);
        await using var target = new SqlConnection(targetConnectionString);
        await source.OpenAsync(cancellationToken);
        await target.OpenAsync(cancellationToken);

        var expectedRowsByTable = fresh.Tables.ToDictionary(x => x.Table, x => x.SourceRows, StringComparer.OrdinalIgnoreCase);
        await RunIamPhaseAsync(source, target, cancellationToken);

        foreach (var phase in TestDataMigrationPlan.CopyPhases)
        {
            await using var transaction = (SqlTransaction)await target.BeginTransactionAsync(cancellationToken);
            try
            {
                foreach (var tableName in phase)
                {
                    if (tableName.Equals("UserRoles", StringComparison.OrdinalIgnoreCase))
                    {
                        await CopyUserRolesAsync(source, target, transaction, cancellationToken);
                    }
                    else
                    {
                        await CopyTableAsync(source, target, transaction, TestDataMigrationPlan.For(tableName), cancellationToken);
                    }
                }

                await ValidatePhaseCountsAsync(target, transaction, phase, expectedRowsByTable, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        Console.WriteLine("Test Data Bootstrap apply completed. Run verify before using TARGET_SQL2016_DB.");
        return 0;
    }

    private static bool MatchesReport(PreflightReport expected, PreflightReport actual)
    {
        if (!string.Equals(expected.ToolVersion, TestDataMigrationPlan.ToolVersion, StringComparison.Ordinal)
            || !string.Equals(expected.PlanFingerprint, TestDataMigrationPlan.PlanFingerprint, StringComparison.Ordinal)
            || !string.Equals(actual.ToolVersion, TestDataMigrationPlan.ToolVersion, StringComparison.Ordinal)
            || !string.Equals(actual.PlanFingerprint, TestDataMigrationPlan.PlanFingerprint, StringComparison.Ordinal)
            || !string.Equals(expected.SourceLabel, TestDataMigrationPlan.SourceLabel, StringComparison.Ordinal)
            || !string.Equals(expected.TargetLabel, TestDataMigrationPlan.TargetLabel, StringComparison.Ordinal))
        {
            return false;
        }

        var expectedTables = expected.Tables.ToDictionary(x => x.Table, StringComparer.OrdinalIgnoreCase);
        foreach (var table in actual.Tables)
        {
            if (!expectedTables.TryGetValue(table.Table, out var expectedTable))
            {
                return false;
            }

            if (expectedTable.SourceRows != table.SourceRows
                || expectedTable.TargetRows != table.TargetRows
                || expectedTable.SourceSchemaFingerprint != table.SourceSchemaFingerprint
                || expectedTable.TargetSchemaFingerprint != table.TargetSchemaFingerprint)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task CopyTableAsync(SqlConnection source, SqlConnection target, SqlTransaction transaction, TablePlan plan, CancellationToken cancellationToken)
    {
        var columns = await ReadWritableColumnsAsync(target, transaction, plan, cancellationToken);
        if (columns.Count == 0)
        {
            return;
        }

        var columnList = string.Join(", ", columns.Select(QuoteName));
        await using var sourceCommand = new SqlCommand($"SELECT {columnList} FROM {QuoteName(plan.Schema)}.{QuoteName(plan.Table)};", source);
        await using var reader = await sourceCommand.ExecuteReaderAsync(cancellationToken);

        using var bulkCopy = new SqlBulkCopy(target, SqlBulkCopyOptions.CheckConstraints, transaction)
        {
            DestinationTableName = $"{QuoteName(plan.Schema)}.{QuoteName(plan.Table)}",
            BatchSize = 500
        };

        foreach (var column in columns)
        {
            bulkCopy.ColumnMappings.Add(column, column);
        }

        await bulkCopy.WriteToServerAsync(reader, cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> ReadWritableColumnsAsync(SqlConnection target, SqlTransaction transaction, TablePlan plan, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.name
            FROM sys.columns AS c
            INNER JOIN sys.tables AS t ON t.object_id = c.object_id
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE s.name = @schema AND t.name = @table AND c.is_computed = 0
            ORDER BY c.column_id;
            """;

        var columns = new List<string>();
        await using var command = new SqlCommand(sql, target, transaction);
        command.Parameters.AddWithValue("@schema", plan.Schema);
        command.Parameters.AddWithValue("@table", plan.Table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static async Task RunIamPhaseAsync(SqlConnection source, SqlConnection target, CancellationToken cancellationToken)
    {
        await using var transaction = (SqlTransaction)await target.BeginTransactionAsync(cancellationToken);
        try
        {
            await ReconcileSystemIamAsync(target, transaction, cancellationToken);
            await CopyCustomRolesAsync(source, target, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task ReconcileSystemIamAsync(SqlConnection target, SqlTransaction transaction, CancellationToken cancellationToken)
    {
        var permissionIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var permission in PermissionCatalog.All)
        {
            var id = await UpsertPermissionAsync(target, transaction, permission, cancellationToken);
            permissionIds[permission.Name] = id;
        }

        foreach (var role in SystemRoleCatalog.All)
        {
            var roleId = await UpsertSystemRoleAsync(target, transaction, role, cancellationToken);
            await DeleteRolePermissionsAsync(target, transaction, roleId, cancellationToken);
            foreach (var permissionName in PermissionSeedService.GetPermissionsForRole(role).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (permissionIds.TryGetValue(permissionName, out var permissionId))
                {
                    await InsertRolePermissionAsync(target, transaction, roleId, permissionId, cancellationToken);
                }
            }
        }
    }

    private static async Task<Guid> UpsertPermissionAsync(SqlConnection target, SqlTransaction transaction, PermissionCatalog.PermissionCatalogEntry permission, CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @id uniqueidentifier = (SELECT Id FROM dbo.Permissions WHERE Name = @name);
            IF @id IS NULL
            BEGIN
                SET @id = NEWID();
                INSERT INTO dbo.Permissions (Id, Name, Capability, DescriptionAr, DescriptionEn, IsActive, CreatedAtUtc, UpdatedAtUtc)
                VALUES (@id, @name, @capability, @descriptionAr, @descriptionEn, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
            END
            SELECT @id;
            """;

        await using var command = new SqlCommand(sql, target, transaction);
        command.Parameters.AddWithValue("@name", permission.Name);
        command.Parameters.AddWithValue("@capability", permission.Capability);
        command.Parameters.AddWithValue("@descriptionAr", (object?)permission.DescriptionAr ?? DBNull.Value);
        command.Parameters.AddWithValue("@descriptionEn", (object?)permission.DescriptionEn ?? DBNull.Value);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken) ?? Guid.Empty);
    }

    private static async Task<Guid> UpsertSystemRoleAsync(SqlConnection target, SqlTransaction transaction, UserRole role, CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @id uniqueidentifier = (SELECT Id FROM dbo.AppRoles WHERE Role = @role);
            IF @id IS NULL
            BEGIN
                SET @id = NEWID();
                INSERT INTO dbo.AppRoles (Id, Role, Name, Description, IsSystemRole, IsActive, CreatedAtUtc, UpdatedAtUtc)
                VALUES (@id, @role, @name, @description, 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
            END
            SELECT @id;
            """;

        await using var command = new SqlCommand(sql, target, transaction);
        var roleValue = ToRoleValue(role);
        command.Parameters.AddWithValue("@role", roleValue);
        command.Parameters.AddWithValue("@name", roleValue);
        command.Parameters.AddWithValue("@description", $"System role: {roleValue}");
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken) ?? Guid.Empty);
    }

    private static async Task DeleteRolePermissionsAsync(SqlConnection target, SqlTransaction transaction, Guid roleId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("DELETE FROM dbo.RolePermissions WHERE AppRoleId = @roleId;", target, transaction);
        command.Parameters.AddWithValue("@roleId", roleId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertRolePermissionAsync(SqlConnection target, SqlTransaction transaction, Guid roleId, Guid permissionId, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.RolePermissions (AppRoleId, PermissionId, CreatedAtUtc, UpdatedAtUtc)
            VALUES (@roleId, @permissionId, SYSUTCDATETIME(), SYSUTCDATETIME());
            """;

        await using var command = new SqlCommand(sql, target, transaction);
        command.Parameters.AddWithValue("@roleId", roleId);
        command.Parameters.AddWithValue("@permissionId", permissionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CopyCustomRolesAsync(SqlConnection source, SqlConnection target, SqlTransaction transaction, CancellationToken cancellationToken)
    {
        await EnsureCustomRolesAreSafeAsync(source, cancellationToken);

        const string sql = """
            SELECT Id, Role, Name, Description, IsSystemRole, IsActive, CreatedAtUtc, UpdatedAtUtc
            FROM dbo.AppRoles
            WHERE IsSystemRole = 0 AND Role IS NULL;
            """;

        await using var command = new SqlCommand(sql, source);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        using var bulkCopy = new SqlBulkCopy(target, SqlBulkCopyOptions.CheckConstraints, transaction)
        {
            DestinationTableName = "dbo.AppRoles"
        };
        foreach (var column in new[] { "Id", "Role", "Name", "Description", "IsSystemRole", "IsActive", "CreatedAtUtc", "UpdatedAtUtc" })
        {
            bulkCopy.ColumnMappings.Add(column, column);
        }

        await bulkCopy.WriteToServerAsync(reader, cancellationToken);
    }

    private static async Task EnsureCustomRolesAreSafeAsync(SqlConnection source, CancellationToken cancellationToken)
    {
        const string invalidSql = """
            SELECT
                (SELECT COUNT_BIG(*) FROM dbo.AppRoles WHERE IsSystemRole = 0 AND Role IS NOT NULL)
              + (SELECT COUNT_BIG(*) FROM (SELECT Name FROM dbo.AppRoles WHERE IsSystemRole = 0 GROUP BY Name HAVING COUNT_BIG(*) > 1) AS d);
            """;

        await using var command = new SqlCommand(invalidSql, source);
        var invalidCount = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        if (invalidCount != 0)
        {
            throw new InvalidOperationException("Custom roles contain unsafe natural-key data.");
        }
    }

    private static async Task CopyUserRolesAsync(SqlConnection source, SqlConnection target, SqlTransaction transaction, CancellationToken cancellationToken)
    {
        const string sourceSql = """
            SELECT ur.AppUserId, ar.Role, ar.Name
            FROM dbo.UserRoles AS ur
            INNER JOIN dbo.AppRoles AS ar ON ar.Id = ur.AppRoleId;
            """;

        var rows = new List<(Guid UserId, string? Role, string Name)>();
        await using (var sourceCommand = new SqlCommand(sourceSql, source))
        await using (var reader = await sourceCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var roleValue = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (roleValue is not null && !IsKnownExactSystemRole(roleValue))
                {
                    throw new InvalidOperationException("A source user role contains an unknown system role value.");
                }

                rows.Add((reader.GetGuid(0), roleValue, reader.GetString(2)));
            }
        }

        foreach (var row in rows)
        {
            const string targetRoleSql = """
                SELECT Id
                FROM dbo.AppRoles
                WHERE (@role IS NOT NULL AND Role = @role)
                   OR (@role IS NULL AND Role IS NULL AND Name = @name);
                """;
            await using var roleCommand = new SqlCommand(targetRoleSql, target, transaction);
            roleCommand.Parameters.AddWithValue("@role", (object?)row.Role ?? DBNull.Value);
            roleCommand.Parameters.AddWithValue("@name", row.Name);
            var targetRoleIds = new List<Guid>();
            await using (var roleReader = await roleCommand.ExecuteReaderAsync(cancellationToken))
            {
                while (await roleReader.ReadAsync(cancellationToken))
                {
                    targetRoleIds.Add(roleReader.GetGuid(0));
                }
            }

            if (targetRoleIds.Count != 1)
            {
                throw new InvalidOperationException("A source user role could not be mapped by natural key.");
            }

            await using var insertCommand = new SqlCommand("INSERT INTO dbo.UserRoles (AppUserId, AppRoleId) VALUES (@userId, @roleId);", target, transaction);
            insertCommand.Parameters.AddWithValue("@userId", row.UserId);
            insertCommand.Parameters.AddWithValue("@roleId", targetRoleIds[0]);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ValidatePhaseCountsAsync(
        SqlConnection target,
        SqlTransaction transaction,
        IReadOnlyList<string> phase,
        IReadOnlyDictionary<string, long> expectedRowsByTable,
        CancellationToken cancellationToken)
    {
        foreach (var tableName in phase)
        {
            var plan = TestDataMigrationPlan.For(tableName);
            var expected = expectedRowsByTable.GetValueOrDefault(plan.FullName);
            var actual = await ReadTargetCountAsync(target, transaction, plan, cancellationToken);
            if (actual != expected)
            {
                throw new InvalidOperationException("A copy phase row-count validation failed.");
            }
        }
    }

    private static async Task<long> ReadTargetCountAsync(SqlConnection target, SqlTransaction transaction, TablePlan plan, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand($"SELECT COUNT_BIG(*) FROM {QuoteName(plan.Schema)}.{QuoteName(plan.Table)};", target, transaction);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static string ToRoleValue(UserRole role) => role.ToString();

    private static bool IsKnownExactSystemRole(string roleValue) =>
        SystemRoleCatalog.All.Any(role => string.Equals(ToRoleValue(role), roleValue, StringComparison.Ordinal));

    private static string QuoteName(string value) => $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";
}
