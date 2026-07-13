namespace ProductionLinePlanner.Domain.Authorization;

public static class FactoryStructurePermissions
{
    public const string View = "factory-structure.view";
    public const string Manage = "factory-structure.manage";

    public static string ForHttpMethod(string method) => method.ToUpperInvariant() switch
    {
        "GET" => View,
        "POST" or "PUT" or "PATCH" or "DELETE" => Manage,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unsupported factory-structure HTTP method.")
    };
}
