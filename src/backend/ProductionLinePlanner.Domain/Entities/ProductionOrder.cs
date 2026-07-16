using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Entities;

public class ProductionOrder
{
    private ProductionOrder() { }

    public ProductionOrder(Guid id, string orderNumber, Guid productModelId, Guid? productionLineId,
        DateOnly productionDate, decimal plannedQuantity, string? notes, Guid createdBy, DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(orderNumber)) throw new ArgumentException("Order number is required.", nameof(orderNumber));
        if (productModelId == Guid.Empty) throw new ArgumentException("Product model is required.", nameof(productModelId));
        if (plannedQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(plannedQuantity));
        Id = id; OrderNumber = orderNumber.Trim(); ProductModelId = productModelId; ProductionLineId = productionLineId;
        ProductionDate = productionDate; PlannedQuantity = plannedQuantity; Notes = Normalize(notes); CreatedBy = createdBy;
        CreatedAtUtc = createdAtUtc; UpdatedAtUtc = createdAtUtc; UpdatedBy = createdBy; RecordedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public string OrderNumber { get; private set; } = string.Empty;
    public Guid ProductModelId { get; private set; }
    public ProductModel? ProductModel { get; set; }
    public Guid? ProductionLineId { get; private set; }
    public ProductionLine? ProductionLine { get; set; }
    public DateOnly ProductionDate { get; private set; }
    /// <summary>When this production day was recorded, deliberately independent from ProductionDate.</summary>
    public DateTime RecordedAtUtc { get; private set; }
    public Guid? SourceImportBatchId { get; private set; }
    public ImportBatch? SourceImportBatch { get; set; }
    public string? SourceReference { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public decimal PlannedQuantity { get; private set; }
    public ProductionOrderStatus Status { get; private set; } = ProductionOrderStatus.Draft;
    public string? Notes { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid UpdatedBy { get; private set; }
    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();
    public List<StageProductionRecord> StageProductionRecords { get; } = [];
    public List<ProductionDayStageResolution> StageResolutions { get; } = [];

    public void MarkImported(Guid importBatchId, string sourceReference, DateTime recordedAtUtc)
    {
        if (importBatchId == Guid.Empty) throw new ArgumentException("Import batch is required.", nameof(importBatchId));
        if (string.IsNullOrWhiteSpace(sourceReference)) throw new ArgumentException("Source reference is required.", nameof(sourceReference));
        SourceImportBatchId = importBatchId;
        SourceReference = sourceReference.Trim();
        RecordedAtUtc = recordedAtUtc;
    }

    /// <summary>
    /// Marks a manually entered daily operation without treating it as an
    /// imported batch. The correlation reference makes retrying the same save
    /// safe while RecordedAt remains independent from ProductionDate.
    /// </summary>
    public void MarkDailyOperation(string sourceReference, DateTime recordedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(sourceReference))
            throw new ArgumentException("Source reference is required.", nameof(sourceReference));

        SourceReference = sourceReference.Trim();
        RecordedAtUtc = recordedAtUtc;
    }

    public void UpdateDraft(DateOnly productionDate, decimal plannedQuantity, string? notes, Guid actorId, DateTime atUtc)
    {
        if (Status != ProductionOrderStatus.Draft) throw new InvalidOperationException("Only draft production orders can be edited.");
        if (plannedQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(plannedQuantity));
        ProductionDate = productionDate; PlannedQuantity = plannedQuantity; Notes = Normalize(notes); Touch(actorId, atUtc);
    }
    public void Activate(Guid actorId, DateTime atUtc)
    {
        if (Status != ProductionOrderStatus.Draft) throw new InvalidOperationException("Only draft production orders can be activated.");
        Status = ProductionOrderStatus.Active; Touch(actorId, atUtc);
    }
    public void Complete(Guid actorId, DateTime atUtc)
    {
        if (Status != ProductionOrderStatus.Active) throw new InvalidOperationException("Only active production orders can be completed.");
        Status = ProductionOrderStatus.Completed; Touch(actorId, atUtc);
    }
    public void ApproveDay(Guid actorId, DateTime atUtc)
    {
        if (Status != ProductionOrderStatus.Draft) throw new InvalidOperationException("Only draft production days can be approved.");
        Status = ProductionOrderStatus.Completed; ApprovedBy = actorId; ApprovedAtUtc = atUtc; Touch(actorId, atUtc);
    }
    public void Cancel(Guid actorId, DateTime atUtc)
    {
        if (Status is not (ProductionOrderStatus.Draft or ProductionOrderStatus.Active)) throw new InvalidOperationException("Only draft or active production orders can be cancelled.");
        Status = ProductionOrderStatus.Cancelled; Touch(actorId, atUtc);
    }
    public void Touch(Guid actorId, DateTime atUtc) { UpdatedBy = actorId; UpdatedAtUtc = atUtc; ConcurrencyToken = Guid.NewGuid(); }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
