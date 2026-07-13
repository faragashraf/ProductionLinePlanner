namespace ProductionLinePlanner.Application.Requests;

public sealed class SetWorkerSalaryHistoryRequest
{
    public decimal Amount { get; init; }
    public string CurrencyCode { get; init; } = "EGP";
    public DateTime EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public string? Notes { get; init; }
}
