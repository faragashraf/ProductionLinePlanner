namespace ProductionLinePlanner.Application.Requests;

public sealed class SetWorkerSalaryRequest
{
    public decimal Amount { get; init; }
    public string CurrencyCode { get; init; } = "EGP";
    public DateTime EffectiveFrom { get; init; }
    public string? Notes { get; init; }
}
