using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.DTOs;

public sealed record AttendanceStatusRecord(
    Guid WorkerId,
    AttendanceStatus Status,
    DateTime AttendanceTimeUtc,
    string? Source,
    string? SourceRawId = null);

public sealed record AttendancePresenceWindowDto(
    Guid WorkerId,
    AttendanceStatus Status,
    DateTime? FirstInUtc,
    DateTime? LastOutUtc,
    bool HasSourceCheckIn);
