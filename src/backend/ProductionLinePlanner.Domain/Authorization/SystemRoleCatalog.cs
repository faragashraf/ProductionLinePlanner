using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Authorization;

public static class SystemRoleCatalog
{
    public static IReadOnlyCollection<UserRole> All { get; } = Enum.GetValues<UserRole>();

    public static bool IsSystemRoleName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        All.Any(role => string.Equals(role.ToString(), name.Trim(), StringComparison.OrdinalIgnoreCase));
}
