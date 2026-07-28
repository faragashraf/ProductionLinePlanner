using ProductionLinePlanner.Application.Realtime;
using ProductionLinePlanner.Domain.Authorization;

namespace ProductionLinePlanner.Api.Realtime;

public static class ManufacturingRealtimeGroups
{
    public const string FactoryStructure = "factory-structure";
    public const string Departments = "departments";
    public const string Stages = "stages";
    public const string Models = "models";
    public const string Employees = "employees";
    public const string LineStaffing = "line-staffing";
    public const string DailyProductionOperations = "daily-production-operations";
    public const string ManufacturingCommandCenter = "manufacturing-command-center";
    public const string AttendanceWorkforce = "attendance-workforce";
    public const string Reports = "reports";

    public static string ForScreen(string screen) => $"manufacturing:{screen.Trim().ToLowerInvariant()}";

    public static bool TryGetRequiredPermission(string? screen, out string permission)
    {
        permission = screen?.Trim().ToLowerInvariant() switch
        {
            FactoryStructure => FactoryStructurePermissions.View,
            Departments => "departments.view",
            Stages => "stages.view",
            Models => "models.view",
            Employees => "workers.view",
            LineStaffing => "assignments.view",
            DailyProductionOperations => "production.record",
            ManufacturingCommandCenter => "production.view",
            AttendanceWorkforce => "attendance.view",
            Reports => "reports.production.view",
            _ => string.Empty
        };
        return !string.IsNullOrEmpty(permission);
    }

    public static IReadOnlyCollection<string> ForChange(ManufacturingDataChanged change) => change.EntityType switch
    {
        ManufacturingEntityType.Factory or ManufacturingEntityType.ProductionLine => [ForScreen(FactoryStructure), ForScreen(Stages), ForScreen(ManufacturingCommandCenter)],
        ManufacturingEntityType.Department => [ForScreen(FactoryStructure), ForScreen(Departments), ForScreen(Stages), ForScreen(ManufacturingCommandCenter)],
        ManufacturingEntityType.MainStage or ManufacturingEntityType.SubStage => [ForScreen(Stages), ForScreen(Models), ForScreen(ManufacturingCommandCenter)],
        ManufacturingEntityType.ProductModel or ManufacturingEntityType.ProductModelStage => [ForScreen(Models), ForScreen(ManufacturingCommandCenter)],
        ManufacturingEntityType.ProductionOrder or ManufacturingEntityType.StageProductionRecord => [ForScreen(DailyProductionOperations), ForScreen(ManufacturingCommandCenter), ForScreen(Reports)],
        ManufacturingEntityType.AttendanceRecord => [ForScreen(AttendanceWorkforce), ForScreen(DailyProductionOperations), ForScreen(ManufacturingCommandCenter)],
        ManufacturingEntityType.Worker => WorkerGroups(change),
        ManufacturingEntityType.WorkerDefaultAssignment => [ForScreen(LineStaffing), ForScreen(DailyProductionOperations), ForScreen(ManufacturingCommandCenter)],
        _ => []
    };

    private static IReadOnlyCollection<string> WorkerGroups(ManufacturingDataChanged change)
    {
        var groups = new List<string> { ForScreen(Employees) };
        var kinds = change.WorkerChangeKinds ?? [];

        // The organizational department is currently rendered only by the
        // Workers capability. Staffing, daily operations, command-center, and
        // department catalog queries do not consume that relationship. Older
        // API events without change kinds remain conservatively compatible.
        var affectsOperationalWorkerViews = kinds.Count == 0 || kinds.Any(kind =>
            kind.Equals("created", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("deleted", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("employment-status", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("profile", StringComparison.OrdinalIgnoreCase));
        if (affectsOperationalWorkerViews)
        {
            groups.Add(ForScreen(AttendanceWorkforce));
            groups.Add(ForScreen(LineStaffing));
            groups.Add(ForScreen(DailyProductionOperations));
            groups.Add(ForScreen(ManufacturingCommandCenter));
        }

        return groups;
    }
}
