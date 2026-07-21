using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Entities;

public class StageProductionRecord
{
    private StageProductionRecord() { }

    public StageProductionRecord(Guid id, Guid productionOrderId, Guid productModelStageId, DateOnly productionDate,
        decimal producedQuantity, decimal acceptedQuantity, decimal rejectedQuantity, string stageCode, string stageName,
        decimal piecePrice, decimal? standardSeconds, CompensationMode compensationMode, string productModelCode, string productModelName,
        string factoryCode, string factoryName, string productionLineCode, string productionLineName, string mainStageName,
        Guid clientRequestId, string? notes, Guid actorId, DateTime atUtc)
    {
        ValidateQuantities(producedQuantity, acceptedQuantity, rejectedQuantity);
        Id = id; ProductionOrderId = productionOrderId; ProductModelStageId = productModelStageId; ProductionDate = productionDate;
        ProducedQuantity = producedQuantity; AcceptedQuantity = acceptedQuantity; RejectedQuantity = rejectedQuantity;
        SnapshotStageCode = stageCode; SnapshotStageName = stageName; SnapshotPiecePrice = piecePrice;
        SnapshotStandardSeconds = standardSeconds; SnapshotCompensationMode = compensationMode; Notes = Normalize(notes);
        SnapshotProductModelCode = productModelCode; SnapshotProductModelName = productModelName; ClientRequestId = clientRequestId;
        SnapshotFactoryCode = factoryCode; SnapshotFactoryName = factoryName;
        SnapshotProductionLineCode = productionLineCode; SnapshotProductionLineName = productionLineName;
        SnapshotMainStageName = mainStageName;
        CreatedBy = actorId; CreatedAtUtc = atUtc;
    }

    public Guid Id { get; private set; }
    public Guid ProductionOrderId { get; private set; }
    public ProductionOrder? ProductionOrder { get; set; }
    public Guid ProductModelStageId { get; private set; }
    public ProductModelStage? ProductModelStage { get; set; }
    public DateOnly ProductionDate { get; private set; }
    public decimal ProducedQuantity { get; private set; }
    public decimal AcceptedQuantity { get; private set; }
    public decimal RejectedQuantity { get; private set; }
    public StageProductionRecordStatus Status { get; private set; } = StageProductionRecordStatus.Draft;
    public string SnapshotStageCode { get; private set; } = string.Empty;
    public string SnapshotStageName { get; private set; } = string.Empty;
    public string SnapshotProductModelCode { get; private set; } = string.Empty;
    public string SnapshotProductModelName { get; private set; } = string.Empty;
    public string SnapshotFactoryCode { get; private set; } = string.Empty;
    public string SnapshotFactoryName { get; private set; } = string.Empty;
    public string SnapshotProductionLineCode { get; private set; } = string.Empty;
    public string SnapshotProductionLineName { get; private set; } = string.Empty;
    public string SnapshotMainStageName { get; private set; } = string.Empty;
    public decimal SnapshotPiecePrice { get; private set; }
    public decimal? SnapshotStandardSeconds { get; private set; }
    public CompensationMode SnapshotCompensationMode { get; private set; }
    public decimal TotalWorkerEarnings { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public Guid? CancelledBy { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? ApprovalCancellationReason { get; private set; }
    public Guid ClientRequestId { get; private set; }
    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();
    public List<StageProductionWorkerAllocation> WorkerAllocations { get; } = [];

    public void UpdateDraft(DateOnly productionDate, decimal producedQuantity, decimal acceptedQuantity, decimal rejectedQuantity, string? notes)
    {
        EnsureDraft(); ValidateQuantities(producedQuantity, acceptedQuantity, rejectedQuantity);
        ProductionDate = productionDate; ProducedQuantity = producedQuantity; AcceptedQuantity = acceptedQuantity; RejectedQuantity = rejectedQuantity; Notes = Normalize(notes); ConcurrencyToken = Guid.NewGuid();
    }
    public IReadOnlyCollection<StageProductionWorkerAllocation> ReplaceAllocations(IEnumerable<StageProductionWorkerAllocation> allocations)
    {
        EnsureDraft();
        var replacements = allocations.ToDictionary(x => x.WorkerId);
        var removed = new List<StageProductionWorkerAllocation>();
        foreach (var existing in WorkerAllocations.ToList())
        {
            if (replacements.Remove(existing.WorkerId, out var replacement))
                existing.Update(replacement.Percentage, replacement.FixedAmount, replacement.EquivalentQuantity, replacement.CalculatedEarning, replacement.Notes, replacement.ManualOverrideReason, replacement.InputQuantity);
            else
            {
                WorkerAllocations.Remove(existing);
                removed.Add(existing);
            }
        }
        WorkerAllocations.AddRange(replacements.Values);
        SynchronizeTotalWorkerEarnings();
        ConcurrencyToken = Guid.NewGuid();
        return removed;
    }
    public void Approve(Guid actorId, DateTime atUtc)
    {
        EnsureDraft(); EnsureFinancialConsistency(); Status = StageProductionRecordStatus.Approved; ApprovedBy = actorId; ApprovedAtUtc = atUtc; ConcurrencyToken = Guid.NewGuid();
    }
    public void Approve(decimal totalWorkerEarnings, Guid actorId, DateTime atUtc)
    {
        EnsureDraft();
        if (totalWorkerEarnings != WorkerAllocations.Sum(x => x.CalculatedEarning))
            throw new InvalidOperationException("إجمالي المستحقات لا يطابق مجموع مستحقات العمال المحفوظة.");
        Approve(actorId, atUtc);
    }
    public void SetCalculationPreview(decimal totalWorkerEarnings)
    {
        EnsureDraft();
        if (totalWorkerEarnings != WorkerAllocations.Sum(x => x.CalculatedEarning))
            throw new InvalidOperationException("إجمالي المستحقات لا يطابق مجموع مستحقات العمال المحسوبة.");
        TotalWorkerEarnings = totalWorkerEarnings;
    }
    public void CancelProductionApproval(string reason, Guid actorId, DateTime atUtc)
    {
        if (Status != StageProductionRecordStatus.Approved) throw new InvalidOperationException("Only approved records can be cancelled.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("سبب إلغاء اعتماد الإنتاج مطلوب.", nameof(reason));
        Status = StageProductionRecordStatus.Cancelled; CancelledBy = actorId; CancelledAtUtc = atUtc; ApprovalCancellationReason = reason.Trim(); ConcurrencyToken = Guid.NewGuid();
    }
    public void ReopenDailyDraftAfterApprovalCancellation()
    {
        if (Status != StageProductionRecordStatus.Cancelled || string.IsNullOrWhiteSpace(ApprovalCancellationReason))
            throw new InvalidOperationException("Only cancelled daily records can be reopened for correction.");
        Status = StageProductionRecordStatus.Draft; ConcurrencyToken = Guid.NewGuid();
    }
    public void EnsureFinancialConsistency()
    {
        var allocationTotal = WorkerAllocations.Sum(x => x.CalculatedEarning);
        if (TotalWorkerEarnings != allocationTotal)
            throw new InvalidOperationException("إجمالي المستحقات لا يطابق مجموع مستحقات العمال المحفوظة.");
    }
    private void SynchronizeTotalWorkerEarnings() => TotalWorkerEarnings = WorkerAllocations.Sum(x => x.CalculatedEarning);
    private void EnsureDraft() { if (Status != StageProductionRecordStatus.Draft) throw new InvalidOperationException("Only draft records can be changed."); }
    private static void ValidateQuantities(decimal produced, decimal accepted, decimal rejected)
    {
        if (produced < 0 || accepted < 0 || rejected < 0 || accepted + rejected > produced) throw new ArgumentException("Accepted plus rejected quantity must not exceed produced quantity.");
    }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
