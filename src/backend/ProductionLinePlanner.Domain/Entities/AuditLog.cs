using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Entities;

public class AuditLog
{
    private AuditLog() { }

    public AuditLog(
        Guid id,
        Guid actorUserId,
        AuditActionType actionType,
        string entityType,
        string entityId,
        string? entityBeforeJson = null,
        string? entityAfterJson = null,
        string? requestMeta = null,
        DateTime? createdAtUtc = null)
    {
        if (actorUserId == Guid.Empty)
            throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentException("EntityType is required.", nameof(entityType));
        if (string.IsNullOrWhiteSpace(entityId))
            throw new ArgumentException("EntityId is required.", nameof(entityId));

        Id = id;
        ActorUserId = actorUserId;
        ActionType = actionType;
        EntityType = entityType.Trim();
        EntityId = entityId.Trim();
        EntityBeforeJson = string.IsNullOrWhiteSpace(entityBeforeJson) ? null : entityBeforeJson.Trim();
        EntityAfterJson = string.IsNullOrWhiteSpace(entityAfterJson) ? null : entityAfterJson.Trim();
        RequestMeta = string.IsNullOrWhiteSpace(requestMeta) ? null : requestMeta.Trim();
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
    }

    public Guid Id { get; init; }
    public Guid ActorUserId { get; private set; }
    public AppUser? ActorUser { get; set; }
    public AuditActionType ActionType { get; private set; }
    public string EntityType { get; private set; }
    public string EntityId { get; private set; }
    public string? EntityBeforeJson { get; private set; }
    public string? EntityAfterJson { get; private set; }
    public string? RequestMeta { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}
