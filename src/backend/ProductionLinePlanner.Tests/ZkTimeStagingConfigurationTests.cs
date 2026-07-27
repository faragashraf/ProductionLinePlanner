using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProductionLinePlanner.Api.HostedServices;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Infrastructure;
using ProductionLinePlanner.Infrastructure.Attendance;
using ProductionLinePlanner.Infrastructure.Attendance.Services;

namespace ProductionLinePlanner.Tests;

public sealed class ZkTimeStagingConfigurationTests
{
    [Fact]
    public void Startup_registration_uses_the_same_normalized_staging_decisions_as_infrastructure()
    {
        Assert.Equal(AttendanceSourceOptions.StagingMode, AttendanceSourceOptions.ResolveMode(" Staging "));
        Assert.True(AttendanceSourceOptions.ResolveStagingProcessorEnabled("not-a-boolean"));
        Assert.Equal(15, AttendanceSourceOptions.ResolveStagingProcessorIntervalSeconds("0"));
    }

    [Fact]
    public void Direct_mode_resolves_direct_source_without_accessing_staging_schema()
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:AppDatabase"] = "Server=localhost;Database=Dayoub;Integrated Security=true;TrustServerCertificate=true",
            ["ConnectionStrings:AttendanceDatabase"] = "Server=localhost;Database=ZKTime;Integrated Security=true;TrustServerCertificate=true",
            ["AttendanceSource:Mode"] = "Direct"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var source = scope.ServiceProvider.GetRequiredService<IAttendanceSource>();

        Assert.IsType<ZkTimeDirectAttendanceSource>(source);
    }

    [Fact]
    public void Staging_mode_resolves_staging_source_for_attendance_and_backlog_reads()
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:AppDatabase"] = "Server=localhost;Database=Dayoub;Integrated Security=true;TrustServerCertificate=true",
            ["ConnectionStrings:AttendanceDatabase"] = "Server=localhost;Database=ZKTime;Integrated Security=true;TrustServerCertificate=true",
            ["AttendanceSource:Mode"] = "Staging",
            ["AttendanceSource:StagingProcessorEnabled"] = "true"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<ZkTimeStagingSource>(scope.ServiceProvider.GetRequiredService<IAttendanceSource>());
        Assert.IsType<ZkTimeStagingSource>(scope.ServiceProvider.GetRequiredService<IZkStagingBacklogReader>());
    }

    [Fact]
    public async Task Staging_startup_validation_propagates_clear_missing_schema_error()
    {
        var expected = new InvalidOperationException(
            "AttendanceSource:Mode is 'Staging', but the required ZKTime staging schema is not installed.");
        var validator = new FailingValidator(expected);
        var service = new ZkStagingSchemaValidationHostedService(
            validator,
            NullLogger<ZkStagingSchemaValidationHostedService>.Instance);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.Contains("not installed", actual.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, validator.CallCount);
    }

    [Fact]
    public void Staging_schema_contract_reports_missing_and_outdated_installations()
    {
        var missing = Assert.Throws<InvalidOperationException>(() => ZkTimeStagingSchema.EnsureCompatible(false, 0));
        var outdated = Assert.Throws<InvalidOperationException>(() => ZkTimeStagingSchema.EnsureCompatible(true, 0));

        Assert.Contains("not installed", missing.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("version 0", outdated.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"version {ZkTimeStagingSchema.CurrentVersion}", outdated.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FailingValidator(Exception exception) : IZkStagingSchemaValidator
    {
        public int CallCount { get; private set; }

        public Task ValidateAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromException(exception);
        }
    }
}
