using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Engines;

public interface IWorkerDepartmentAssignmentEngine
{
    Task<Result<WorkerDepartmentAssignmentDto>> AssignAsync(
        Guid workerId,
        Guid departmentId,
        Guid expectedConcurrencyToken,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);
}
