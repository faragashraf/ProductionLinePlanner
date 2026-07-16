namespace ProductionLinePlanner.Infrastructure.Attendance.Entities;

using System.ComponentModel.DataAnnotations.Schema;

public sealed class AttendanceSourceUserInfo
{
    public int? UserId { get; init; }
    public string? BadgeNumber { get; init; }
    public string? Name { get; init; }
    public short? DefaultDeptId { get; init; }
    public byte[]? Photo { get; init; }

    [NotMapped]
    public int? DepartmentId => DefaultDeptId.HasValue ? (int)DefaultDeptId.Value : null;
}
