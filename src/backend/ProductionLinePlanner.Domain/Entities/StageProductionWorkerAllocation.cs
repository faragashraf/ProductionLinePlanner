namespace ProductionLinePlanner.Domain.Entities;

public class StageProductionWorkerAllocation
{
    private StageProductionWorkerAllocation() { }
    public StageProductionWorkerAllocation(Guid id, Guid workerId, decimal? percentage, decimal? fixedAmount, string? notes)
    { Id = id; WorkerId = workerId; Percentage = percentage; FixedAmount = fixedAmount; Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(); }
    public Guid Id { get; private set; }
    public Guid StageProductionRecordId { get; private set; }
    public StageProductionRecord? StageProductionRecord { get; set; }
    public Guid WorkerId { get; private set; }
    public Worker? Worker { get; set; }
    public decimal? Percentage { get; private set; }
    public decimal? FixedAmount { get; private set; }
    public decimal EquivalentQuantity { get; private set; }
    public decimal CalculatedEarning { get; private set; }
    public string? Notes { get; private set; }
    public void SetCalculatedAmounts(decimal equivalentQuantity, decimal calculatedEarning) { EquivalentQuantity = equivalentQuantity; CalculatedEarning = calculatedEarning; }
    public void Update(decimal? percentage, decimal? fixedAmount, decimal equivalentQuantity, decimal calculatedEarning, string? notes)
    {
        Percentage = percentage; FixedAmount = fixedAmount; EquivalentQuantity = equivalentQuantity; CalculatedEarning = calculatedEarning; Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }
}
