namespace ProductionLinePlanner.Domain.Entities;

/// <summary>An explicit decision for a required stage that did not run; it never creates zero production.</summary>
public class ProductionDayStageResolution
{
    private ProductionDayStageResolution() { }

    public ProductionDayStageResolution(Guid id, Guid productionOrderId, Guid productModelStageId, string reason, Guid resolvedBy, DateTime resolvedAtUtc)
    {
        if (productionOrderId == Guid.Empty) throw new ArgumentException("ProductionOrderId is required.", nameof(productionOrderId));
        if (productModelStageId == Guid.Empty) throw new ArgumentException("ProductModelStageId is required.", nameof(productModelStageId));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reason is required.", nameof(reason));
        if (resolvedBy == Guid.Empty) throw new ArgumentException("ResolvedBy is required.", nameof(resolvedBy));
        Id = id;
        ProductionOrderId = productionOrderId;
        ProductModelStageId = productModelStageId;
        Reason = reason.Trim();
        ResolvedBy = resolvedBy;
        ResolvedAtUtc = resolvedAtUtc;
    }

    public Guid Id { get; init; }
    public Guid ProductionOrderId { get; init; }
    public ProductionOrder? ProductionOrder { get; set; }
    public Guid ProductModelStageId { get; init; }
    public ProductModelStage? ProductModelStage { get; set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid ResolvedBy { get; private set; }
    public DateTime ResolvedAtUtc { get; private set; }
}
