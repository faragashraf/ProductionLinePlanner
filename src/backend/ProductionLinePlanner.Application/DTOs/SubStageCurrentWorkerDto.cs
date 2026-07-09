using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.DTOs;

public sealed class SubStageCurrentWorkerDto
{
    public Guid WorkerId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string EmployeeCode { get; init; } = string.Empty;
    public AssignmentType AssignmentType { get; init; }
    public Guid? FromSubStageId { get; init; }
    public Guid? ReplacementForWorkerId { get; init; }
}
