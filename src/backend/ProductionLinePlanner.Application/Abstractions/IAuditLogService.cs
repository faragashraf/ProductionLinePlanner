using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.Abstractions;

public interface IAuditLogService
{
    Task<Result> RecordAsync(
        Guid actorUserId,
        AuditActionType actionType,
        string entityType,
        string entityId,
        string? entityBeforeJson = null,
        string? entityAfterJson = null,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);
}
