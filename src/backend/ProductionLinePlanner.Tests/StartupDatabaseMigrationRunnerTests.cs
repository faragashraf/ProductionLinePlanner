using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Api.Database;

namespace ProductionLinePlanner.Tests;

public sealed class StartupDatabaseMigrationRunnerTests
{
    [Fact]
    public async Task Disabled_configuration_does_not_query_or_apply_migrations_and_logs_the_reason()
    {
        var executor = new RecordingMigrationExecutor();
        var logger = new RecordingLogger<StartupDatabaseMigrationRunner>();
        var runner = CreateRunner(false, 120, executor, logger);

        await runner.ApplyIfEnabledAsync();

        Assert.Equal(0, executor.SetTimeoutCalls);
        Assert.Equal(0, executor.GetPendingCalls);
        Assert.Equal(0, executor.MigrateCalls);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("disabled by configuration", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Enabled_configuration_with_no_pending_migrations_completes_without_migrating()
    {
        var executor = new RecordingMigrationExecutor();
        var logger = new RecordingLogger<StartupDatabaseMigrationRunner>();
        var runner = CreateRunner(true, 240, executor, logger);

        await runner.ApplyIfEnabledAsync();

        Assert.Equal(240, executor.CommandTimeoutSeconds);
        Assert.Equal(1, executor.GetPendingCalls);
        Assert.Equal(0, executor.MigrateCalls);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("no pending migrations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Enabled_configuration_applies_pending_migrations_and_logs_the_reviewed_ids()
    {
        var executor = new RecordingMigrationExecutor
        {
            PendingMigrations = ["20260719213301_AddNotificationPolicyPlatform"]
        };
        var logger = new RecordingLogger<StartupDatabaseMigrationRunner>();
        var runner = CreateRunner(true, 120, executor, logger);

        await runner.ApplyIfEnabledAsync();

        Assert.Equal(1, executor.GetPendingCalls);
        Assert.Equal(1, executor.MigrateCalls);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("20260719213301_AddNotificationPolicyPlatform", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("completed successfully", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Migration_failure_is_rethrown_and_the_log_message_does_not_expose_sensitive_exception_text()
    {
        const string sensitiveText = "Password=not-logged";
        var executor = new RecordingMigrationExecutor
        {
            PendingMigrations = ["20260719213301_AddNotificationPolicyPlatform"],
            MigrateException = new InvalidOperationException(sensitiveText)
        };
        var logger = new RecordingLogger<StartupDatabaseMigrationRunner>();
        var runner = CreateRunner(true, 120, executor, logger);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ApplyIfEnabledAsync());

        Assert.Equal(sensitiveText, exception.Message);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Critical && entry.Message.Contains("will not start", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry => entry.Message.Contains(sensitiveText, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public void Options_validation_rejects_an_unsafe_command_timeout(int timeout)
    {
        var result = new DatabaseMigrationOptionsValidator().Validate(null, new DatabaseMigrationOptions
        {
            MigrationCommandTimeoutSeconds = timeout
        });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Options_default_to_disabled_and_accept_a_reasonable_timeout()
    {
        var options = new DatabaseMigrationOptions();
        var result = new DatabaseMigrationOptionsValidator().Validate(null, options);

        Assert.False(options.ApplyMigrationsOnStartup);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Options_bind_the_documented_configuration_keys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ApplyMigrationsOnStartup"] = "true",
                ["Database:MigrationCommandTimeoutSeconds"] = "240"
            })
            .Build();

        var options = configuration.GetSection(DatabaseMigrationOptions.SectionName).Get<DatabaseMigrationOptions>();

        Assert.NotNull(options);
        Assert.True(options.ApplyMigrationsOnStartup);
        Assert.Equal(240, options.MigrationCommandTimeoutSeconds);
    }

    private static StartupDatabaseMigrationRunner CreateRunner(
        bool applyOnStartup,
        int timeoutSeconds,
        IStartupDatabaseMigrationExecutor executor,
        ILogger<StartupDatabaseMigrationRunner> logger) =>
        new(
            Options.Create(new DatabaseMigrationOptions
            {
                ApplyMigrationsOnStartup = applyOnStartup,
                MigrationCommandTimeoutSeconds = timeoutSeconds
            }),
            executor,
            logger);

    private sealed class RecordingMigrationExecutor : IStartupDatabaseMigrationExecutor
    {
        public IReadOnlyCollection<string> PendingMigrations { get; init; } = [];
        public Exception? MigrateException { get; init; }
        public int SetTimeoutCalls { get; private set; }
        public int GetPendingCalls { get; private set; }
        public int MigrateCalls { get; private set; }
        public int? CommandTimeoutSeconds { get; private set; }

        public void SetCommandTimeout(int commandTimeoutSeconds)
        {
            SetTimeoutCalls++;
            CommandTimeoutSeconds = commandTimeoutSeconds;
        }

        public Task<IReadOnlyCollection<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
        {
            GetPendingCalls++;
            return Task.FromResult(PendingMigrations);
        }

        public Task MigrateAsync(CancellationToken cancellationToken)
        {
            MigrateCalls++;
            return MigrateException is null ? Task.CompletedTask : Task.FromException(MigrateException);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
