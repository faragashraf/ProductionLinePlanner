namespace ProductionLinePlanner.Application.DTOs;

public sealed class WorkerSalaryHistoryDto
{
    public Guid Id { get; init; }
    public Guid WorkerId { get; init; }
    public decimal Amount { get; init; }
    public string CurrencyCode { get; init; } = "EGP";
    public DateTime EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public string? Notes { get; init; }
    public Guid? CreatedBy { get; init; }
    public Guid? UpdatedBy { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}
