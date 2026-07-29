using Microsoft.Extensions.Configuration;
using ProductionLinePlanner.Infrastructure.Attendance.Services;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Importing;
using ProductionLinePlanner.Infrastructure.Time;

namespace ProductionLinePlanner.Tests;

public sealed class CairoTimeZoneProviderTests
{
    [Theory]
    [InlineData("Africa/Cairo")]
    [InlineData("Egypt Standard Time")]
    public void Uses_a_valid_explicit_configuration(string configuredId)
    {
        var provider = CreateProvider(configuredId);

        Assert.Equal(configuredId, provider.TimeZone.Id);
    }

    [Fact]
    public void Invalid_configuration_falls_back_to_the_platform_default_or_alternate()
    {
        var provider = CreateProvider("invalid/cairo-time-zone");

        Assert.Contains(provider.TimeZone.Id, new[] { "Africa/Cairo", "Egypt Standard Time" });
    }

    [Fact]
    public void Missing_configuration_uses_the_operating_system_preferred_identifier()
    {
        var provider = CreateProvider();
        var expectedId = OperatingSystem.IsWindows() ? "Egypt Standard Time" : "Africa/Cairo";

        Assert.Equal(expectedId, provider.TimeZone.Id);
    }

    [Fact]
    public void Converts_known_cairo_winter_and_dst_times_without_changing_utc_semantics()
    {
        var cairo = CreateProvider().TimeZone;
        var winterUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Unspecified), cairo);
        var summerUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2024, 7, 15, 12, 0, 0, DateTimeKind.Unspecified), cairo);

        Assert.Equal(new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc), winterUtc);
        Assert.Equal(new DateTime(2024, 7, 15, 9, 0, 0, DateTimeKind.Utc), summerUtc);
    }

    [Fact]
    public void Business_services_have_no_static_time_zone_initializers()
    {
        var types = new[]
        {
            typeof(LineStaffingEngine),
            typeof(AttendanceEngine),
            typeof(ProductionReadinessEngine),
            typeof(ProductionCostRecordingService),
            typeof(AttendanceSyncService),
            typeof(AttendanceSyncCoordinator),
            typeof(RealDataIntakeService)
        };

        Assert.All(types, type => Assert.DoesNotContain(
            type.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic),
            field => field.FieldType == typeof(TimeZoneInfo)));
    }

    private static CairoTimeZoneProvider CreateProvider(string? configuredId = null)
    {
        var values = configuredId is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { ["TimeZones:Cairo"] = configuredId };
        return new CairoTimeZoneProvider(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }
}
