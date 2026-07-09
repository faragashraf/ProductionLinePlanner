using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.DTOs;

public sealed record AttendanceStatusRecord(
    Guid WorkerId,
    AttendanceStatus Status,
    DateTime AttendanceTimeUtc,
    string? Source);

