using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Engines;

public interface IProcessedAttendanceOrphanEngine
{
    Task<Result<ProcessedAttendanceOrphanPreviewDto>> PreviewAsync(
        ProcessedAttendanceOrphanQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<ProcessedAttendanceOrphanRepairDto>> RepairAsync(
        Guid actorUserId,
        ProcessedAttendanceOrphanRepairRequest request,
        string? requestMetadata = null,
        CancellationToken cancellationToken = default);
}
