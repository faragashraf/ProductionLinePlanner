using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.Services;

namespace ProductionLinePlanner.Infrastructure.Attendance;

public sealed class AttendanceWorkdayPolicy(
    IOptions<AttendanceSourceOptions> sourceOptions,
    ICairoTimeZoneProvider cairoTimeZoneProvider) : IAttendanceWorkdayPolicy
{
    private readonly AttendanceSourceOptions options = sourceOptions.Value;

    public DateOnly GetOperationalDate(DateTime asOfUtc)
    {
        var utc = asOfUtc.Kind == DateTimeKind.Utc ? asOfUtc : asOfUtc.ToUniversalTime();
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, cairoTimeZoneProvider.TimeZone);
        var date = DateOnly.FromDateTime(local);
        return local.TimeOfDay < options.WorkdayBoundaryTime ? date.AddDays(-1) : date;
    }

    public AttendanceWorkdayWindow GetWindow(DateOnly operationalDate)
    {
        var startLocal = operationalDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified)
            .Add(options.WorkdayBoundaryTime);
        var endLocal = startLocal.AddDays(1);
        return new AttendanceWorkdayWindow(
            operationalDate,
            startLocal,
            endLocal,
            TimeZoneInfo.ConvertTimeToUtc(startLocal, cairoTimeZoneProvider.TimeZone),
            TimeZoneInfo.ConvertTimeToUtc(endLocal, cairoTimeZoneProvider.TimeZone));
    }

    public DateTime GetShiftStartLocal(DateOnly operationalDate) =>
        operationalDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified).Add(options.DayStartTime);
}
