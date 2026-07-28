namespace ProductionLinePlanner.Application.DTOs;

public sealed record WorkerDepartmentAssignmentDto(
    Guid WorkerId,
    Guid DepartmentId,
    string DepartmentName,
    Guid FactoryId,
    string FactoryName,
    Guid ConcurrencyToken,
    DateTime UpdatedAtUtc);
