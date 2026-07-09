using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.DTOs;

public sealed class AttendanceRecordDto
{
    public Guid Id { get; init; }
    public Guid WorkerId { get; init; }
    public DateTime AttendanceTimeUtc { get; init; }
    public AttendanceStatus AttendanceStatus { get; init; }
    public string? Source { get; init; }
    /// <summary>External attendance identifiers from ZKTeco USERINFO.</summary>
    public string? AttendanceUserId { get; init; }
    /// <summary>External attendance identifiers from ZKTeco USERINFO.</summary>
    public string? BadgeNumber { get; init; }
    public string? SourceRawId { get; init; }
}
