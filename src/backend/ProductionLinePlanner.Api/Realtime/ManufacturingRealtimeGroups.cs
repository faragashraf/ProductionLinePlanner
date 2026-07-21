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
    public const string DailyProductionOperations = "daily-production-operations";

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
            DailyProductionOperations => "production.record",
            _ => string.Empty
        };
        return !string.IsNullOrEmpty(permission);
    }

    public static IReadOnlyCollection<string> ForChange(ManufacturingDataChanged change) => change.EntityType switch
    {
        ManufacturingEntityType.Factory or ManufacturingEntityType.ProductionLine => [ForScreen(FactoryStructure), ForScreen(Stages)],
        ManufacturingEntityType.Department => [ForScreen(FactoryStructure), ForScreen(Departments), ForScreen(Stages)],
        ManufacturingEntityType.MainStage or ManufacturingEntityType.SubStage => [ForScreen(Stages), ForScreen(Models)],
        ManufacturingEntityType.ProductModel or ManufacturingEntityType.ProductModelStage => [ForScreen(Models)],
        ManufacturingEntityType.ProductionOrder => [ForScreen(DailyProductionOperations)],
        ManufacturingEntityType.Worker => [ForScreen(Employees)],
        _ => []
    };
}
