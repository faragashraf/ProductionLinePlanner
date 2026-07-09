using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.Engines;

public interface IAuditEngine
{
    Task<Result> RecordAsync(
        Guid actorUserId,
        AuditActionType actionType,
        string entityType,
        string entityId,
        object? before = null,
        object? after = null,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);
}
