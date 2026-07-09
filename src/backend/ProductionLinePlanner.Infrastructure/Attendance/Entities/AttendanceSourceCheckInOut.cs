using System;

namespace ProductionLinePlanner.Infrastructure.Attendance.Entities;

public sealed class AttendanceSourceCheckInOut
{
    public int? UserId { get; init; }
    public DateTime CheckTime { get; init; }
    public string? CheckType { get; init; }
}
