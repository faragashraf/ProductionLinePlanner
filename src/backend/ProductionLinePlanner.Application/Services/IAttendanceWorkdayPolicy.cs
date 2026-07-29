namespace ProductionLinePlanner.Application.Services;

public sealed record AttendanceWorkdayWindow(
    DateOnly OperationalDate,
    DateTime StartLocal,
    DateTime EndLocal,
    DateTime StartUtc,
    DateTime EndUtc);

public interface IAttendanceWorkdayPolicy
{
    DateOnly GetOperationalDate(DateTime asOfUtc);
    AttendanceWorkdayWindow GetWindow(DateOnly operationalDate);
    DateTime GetShiftStartLocal(DateOnly operationalDate);
}
