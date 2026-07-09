namespace ProductionLinePlanner.Application.DTOs;

public sealed class AttendanceSubStageAttendanceDto
{
    public Guid SubStageId { get; init; }
    public string SubStageName { get; init; } = string.Empty;
    public DateTime DateUtc { get; init; }
    public int Capacity { get; init; }
    public int AssignedWorkers { get; init; }
    public int PresentWorkers { get; init; }
    public int LateWorkers { get; init; }
    public int AbsentWorkers { get; init; }
    public int UnassignedWorkers { get; init; }
    public AttendanceWorkerStateDto[] Workers { get; init; } = [];
}
