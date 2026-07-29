using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Api.Database;

/// <summary>
/// Small EF Core boundary that keeps startup migration orchestration testable without a live database.
/// </summary>
public interface IStartupDatabaseMigrationExecutor
{
    void SetCommandTimeout(int commandTimeoutSeconds);

    Task<IReadOnlyCollection<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken);

    Task MigrateAsync(CancellationToken cancellationToken);
}

public sealed class EfCoreStartupDatabaseMigrationExecutor(AppDbContext dbContext) : IStartupDatabaseMigrationExecutor
{
    public void SetCommandTimeout(int commandTimeoutSeconds) =>
        dbContext.Database.SetCommandTimeout(commandTimeoutSeconds);

    public async Task<IReadOnlyCollection<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken) =>
        (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();

    public Task MigrateAsync(CancellationToken cancellationToken) =>
        dbContext.Database.MigrateAsync(cancellationToken);
}

public sealed class StartupDatabaseMigrationRunner(
    IOptions<DatabaseMigrationOptions> options,
    IStartupDatabaseMigrationExecutor executor,
    ILogger<StartupDatabaseMigrationRunner> logger)
{
    public async Task ApplyIfEnabledAsync(CancellationToken cancellationToken = default)
    {
        var migrationOptions = options.Value;
        if (!migrationOptions.ApplyMigrationsOnStartup)
        {
            logger.LogInformation("EF Core startup migration execution is disabled by configuration.");
            return;
        }

        try
        {
            executor.SetCommandTimeout(migrationOptions.MigrationCommandTimeoutSeconds);
            var pendingMigrations = await executor.GetPendingMigrationsAsync(cancellationToken);
            if (pendingMigrations.Count == 0)
            {
                logger.LogInformation("EF Core startup migration execution found no pending migrations.");
                return;
            }

            logger.LogWarning(
                "Applying {PendingMigrationCount} reviewed EF Core migration(s) at startup: {PendingMigrations}.",
                pendingMigrations.Count,
                string.Join(", ", pendingMigrations));

            await executor.MigrateAsync(cancellationToken);
            logger.LogInformation(
                "EF Core startup migration execution completed successfully for {PendingMigrationCount} migration(s).",
                pendingMigrations.Count);
        }
        catch (Exception exception)
        {
            logger.LogCritical(
                "EF Core startup migration execution failed with {ExceptionType}; the application will not start.",
                exception.GetType().Name);
            throw;
        }
    }
}
