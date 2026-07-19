namespace ProductionLinePlanner.Api.Database;

/// <summary>
/// Controls the explicitly opt-in startup migration path for the application database.
/// </summary>
public sealed class DatabaseMigrationOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Applies reviewed EF Core migrations during backend startup when explicitly enabled.
    /// This remains false in all committed configuration, including Production.
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; init; }

    /// <summary>
    /// Command timeout applied only to the startup migration DbContext operations.
    /// </summary>
    public int MigrationCommandTimeoutSeconds { get; init; } = 120;
}
