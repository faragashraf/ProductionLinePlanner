using Microsoft.Extensions.Options;

namespace ProductionLinePlanner.Api.Database;

public sealed class DatabaseMigrationOptionsValidator : IValidateOptions<DatabaseMigrationOptions>
{
    public const int MinimumCommandTimeoutSeconds = 1;
    public const int MaximumCommandTimeoutSeconds = 3600;

    public ValidateOptionsResult Validate(string? name, DatabaseMigrationOptions options)
    {
        if (options.MigrationCommandTimeoutSeconds is < MinimumCommandTimeoutSeconds or > MaximumCommandTimeoutSeconds)
        {
            return ValidateOptionsResult.Fail(
                $"Database:MigrationCommandTimeoutSeconds must be between {MinimumCommandTimeoutSeconds} and {MaximumCommandTimeoutSeconds}.");
        }

        return ValidateOptionsResult.Success;
    }
}
