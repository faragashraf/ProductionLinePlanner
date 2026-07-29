using Microsoft.Extensions.Options;
using ProductionLinePlanner.Infrastructure.Attendance;
using ProductionLinePlanner.Tests.TestInfrastructure;

namespace ProductionLinePlanner.Tests;

public sealed class AttendanceWorkdayPolicyTests
{
    private static readonly AttendanceWorkdayPolicy Policy = new(
        Options.Create(new AttendanceSourceOptions
        {
            WorkdayBoundaryTime = new TimeSpan(5, 0, 0),
            DayStartTime = new TimeSpan(8, 0, 0)
        }),
        TestCairoTimeZoneProvider.Instance);

    [Fact]
    public void Time_before_five_am_cairo_belongs_to_the_previous_operational_day()
    {
        var cairoLocal = new DateTime(2026, 7, 29, 4, 59, 0, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(cairoLocal, TestCairoTimeZoneProvider.Instance.TimeZone);

        Assert.Equal(new DateOnly(2026, 7, 28), Policy.GetOperationalDate(utc));
    }

    [Fact]
    public void Operational_window_converts_five_am_cairo_to_utc_with_the_real_zone_offset()
    {
        var window = Policy.GetWindow(new DateOnly(2026, 7, 29));
        var expectedStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(2026, 7, 29, 5, 0, 0, DateTimeKind.Unspecified),
            TestCairoTimeZoneProvider.Instance.TimeZone);

        Assert.Equal(expectedStartUtc, window.StartUtc);
        Assert.Equal(new DateTime(2026, 7, 29, 2, 0, 0, DateTimeKind.Utc), window.StartUtc);
        Assert.Equal(TimeSpan.FromHours(24), window.EndLocal - window.StartLocal);
        Assert.Equal(new DateTime(2026, 7, 29, 8, 0, 0), Policy.GetShiftStartLocal(new DateOnly(2026, 7, 29)));
    }
}
