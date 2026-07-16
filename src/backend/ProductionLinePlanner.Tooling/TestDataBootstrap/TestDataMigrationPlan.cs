using System.Security.Cryptography;
using System.Text;

namespace ProductionLinePlanner.Tooling.TestDataBootstrap;

public sealed record TablePlan(
    string Schema,
    string Table,
    string Phase,
    bool Include,
    string Decision,
    string KeyStrategy,
    string Notes)
{
    public string FullName => $"{Schema}.{Table}";
}

public static class TestDataMigrationPlan
{
    public const string ToolVersion = "test-data-bootstrap-v2";
    public const int ExpectedApplicationTableCount = 27;
    public const int ExpectedMigrationCount = 17;
    public const string SourceLabel = "SOURCE_TEST_DB";
    public const string TargetLabel = "TARGET_SQL2016_DB";

    public static IReadOnlyList<TablePlan> Tables { get; } =
    [
        new("dbo", "Permissions", "02-iam-baseline", false, "Regenerate", "Natural key: Name", "Product-controlled catalog from code."),
        new("dbo", "AppRoles", "02-iam-baseline", false, "Regenerate system roles; copy custom roles only", "Natural key: Name/Role", "System roles are product-controlled."),
        new("dbo", "RolePermissions", "02-iam-baseline", false, "Regenerate", "Natural keys: Role + Permission", "System role grants are product-controlled."),
        new("dbo", "AppUsers", "07-users-and-custom-roles", true, "Preserve", "Preserve GUID primary keys", "Preserve PasswordHash for this test-data move."),
        new("dbo", "UserRoles", "07-users-and-custom-roles", true, "Preserve with role mapping", "User GUID preserved; role mapped by natural key", "Maps system roles to target-generated IDs."),
        new("dbo", "UserPermissionOverrides", "07-users-and-custom-roles", false, "Exclude", "None", "Empty in source and security-sensitive."),
        new("dbo", "RefreshTokens", "excluded-security", false, "Exclude", "None", "Transient authentication session data."),
        new("dbo", "AuditLogs", "excluded-audit", false, "Exclude", "None", "Excluded from fast initial migration."),
        new("dbo", "AssignmentTimelineEntries", "excluded-history", false, "Exclude", "None", "Not required for FK integrity in the initial migration."),
        new("dbo", "Factories", "03-reference-hierarchy", true, "Preserve", "Preserve GUID primary keys", "Factory setup data."),
        new("dbo", "ProductionLines", "03-reference-hierarchy", true, "Preserve", "Preserve GUID primary keys", "Depends on Factories."),
        new("dbo", "MainStages", "03-reference-hierarchy", true, "Preserve", "Preserve GUID primary keys", "Depends on ProductionLines."),
        new("dbo", "SubStages", "03-reference-hierarchy", true, "Preserve", "Preserve GUID primary keys", "Depends on MainStages."),
        new("dbo", "ProductModels", "04-product-compensation", true, "Preserve", "Preserve GUID primary keys", "Product master data."),
        new("dbo", "ProductModelStages", "04-product-compensation", true, "Preserve", "Preserve GUID primary keys", "Pricing and timing configuration."),
        new("dbo", "Workers", "04-product-compensation", true, "Preserve", "Preserve GUID primary keys", "Worker master data."),
        new("dbo", "WorkerSalaryHistories", "04-product-compensation", true, "Preserve", "Preserve GUID primary keys", "Compensation setup."),
        new("dbo", "WorkerDefaultAssignments", "05-staffing", true, "Preserve", "Preserve GUID primary keys", "Default staffing setup."),
        new("dbo", "WorkerTemporaryAssignments", "05-staffing", true, "Preserve", "Preserve GUID primary keys", "Included for repeatability even when empty."),
        new("dbo", "AttendanceRecords", "06-operational-test-data", true, "Preserve", "Preserve GUID primary keys", "Application-owned attendance snapshots."),
        new("dbo", "ProductionOrders", "06-operational-test-data", true, "Preserve", "Preserve GUID primary keys", "Operational test orders."),
        new("dbo", "StageProductionRecords", "06-operational-test-data", true, "Preserve", "Preserve GUID primary keys", "Operational stage records."),
        new("dbo", "StageProductionWorkerAllocations", "06-operational-test-data", true, "Preserve", "Preserve GUID primary keys", "Worker contribution records."),
        new("dbo", "ImportBatches", "excluded-empty", false, "Exclude", "None", "Empty in source."),
        new("dbo", "ProductionDayStageResolutions", "excluded-empty", false, "Exclude", "None", "Empty in source."),
        new("dbo", "Notifications", "excluded-transient", false, "Exclude", "None", "Transient and empty in source."),
        new("dbo", "StageReadinessSnapshots", "excluded-derived", false, "Regenerate", "None", "Derived/readiness snapshots; empty in source.")
    ];

    public static IReadOnlyList<IReadOnlyList<string>> CopyPhases { get; } =
    [
        ["Factories", "ProductionLines", "MainStages", "SubStages"],
        ["ProductModels", "ProductModelStages", "Workers", "WorkerSalaryHistories"],
        ["WorkerDefaultAssignments", "WorkerTemporaryAssignments"],
        ["AttendanceRecords", "ProductionOrders", "StageProductionRecords", "StageProductionWorkerAllocations"],
        ["AppUsers", "UserRoles"]
    ];

    public static string PlanFingerprint { get; } = ComputePlanFingerprint();

    public static TablePlan For(string tableName) =>
        Tables.First(x => x.Table.Equals(tableName, StringComparison.OrdinalIgnoreCase));

    private static string ComputePlanFingerprint()
    {
        var plan = new StringBuilder();
        plan.AppendLine(ToolVersion);
        foreach (var table in Tables.OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase))
        {
            plan.Append(table.FullName).Append('|')
                .Append(table.Phase).Append('|')
                .Append(table.Include).Append('|')
                .Append(table.Decision).Append('|')
                .Append(table.KeyStrategy).Append('|')
                .AppendLine(table.Notes);
        }

        foreach (var phase in CopyPhases)
        {
            plan.Append("phase|").AppendLine(string.Join(",", phase));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plan.ToString())));
    }
}
