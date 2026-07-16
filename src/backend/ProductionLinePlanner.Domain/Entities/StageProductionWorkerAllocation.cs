namespace ProductionLinePlanner.Domain.Entities;

public class StageProductionWorkerAllocation
{
    private StageProductionWorkerAllocation() { }
    public StageProductionWorkerAllocation(Guid id, Guid workerId, string snapshotWorkerCode, string snapshotWorkerName, decimal? percentage, decimal? fixedAmount, string? notes, string? manualOverrideReason = null, decimal? inputQuantity = null)
    {
        if (string.IsNullOrWhiteSpace(snapshotWorkerCode)) throw new ArgumentException("SnapshotWorkerCode is required.", nameof(snapshotWorkerCode));
        if (string.IsNullOrWhiteSpace(snapshotWorkerName)) throw new ArgumentException("SnapshotWorkerName is required.", nameof(snapshotWorkerName));

        Id = id;
        WorkerId = workerId;
        SnapshotWorkerCode = snapshotWorkerCode.Trim();
        SnapshotWorkerName = snapshotWorkerName.Trim();
        Percentage = percentage;
        FixedAmount = fixedAmount;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        ManualOverrideReason = string.IsNullOrWhiteSpace(manualOverrideReason) ? null : manualOverrideReason.Trim();
        InputQuantity = inputQuantity;
    }
    public Guid Id { get; private set; }
    public Guid StageProductionRecordId { get; private set; }
    public StageProductionRecord? StageProductionRecord { get; set; }
    public Guid WorkerId { get; private set; }
    public Worker? Worker { get; set; }
    public string SnapshotWorkerCode { get; private set; } = string.Empty;
    public string SnapshotWorkerName { get; private set; } = string.Empty;
    public decimal? Percentage { get; private set; }
    public decimal? FixedAmount { get; private set; }
    public decimal EquivalentQuantity { get; private set; }
    public decimal CalculatedEarning { get; private set; }
    public string? Notes { get; private set; }
    public string? ManualOverrideReason { get; private set; }
    /// <summary>Raw workbook allocation quantity. It is never treated as stage or line production.</summary>
    public decimal? InputQuantity { get; private set; }
    public void SetCalculatedAmounts(decimal equivalentQuantity, decimal calculatedEarning) { EquivalentQuantity = equivalentQuantity; CalculatedEarning = calculatedEarning; }
    public void SetManualOverrideReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A manual override reason is required.", nameof(reason));
        ManualOverrideReason = reason.Trim();
    }
    public void Update(decimal? percentage, decimal? fixedAmount, decimal equivalentQuantity, decimal calculatedEarning, string? notes, string? manualOverrideReason, decimal? inputQuantity = null)
    {
        Percentage = percentage; FixedAmount = fixedAmount; EquivalentQuantity = equivalentQuantity; CalculatedEarning = calculatedEarning; Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(); ManualOverrideReason = string.IsNullOrWhiteSpace(manualOverrideReason) ? null : manualOverrideReason.Trim(); InputQuantity = inputQuantity;
    }
}
