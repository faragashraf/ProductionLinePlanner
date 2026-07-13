namespace ProductionLinePlanner.Domain.Entities;

public class WorkerSalaryHistory
{
    private WorkerSalaryHistory() { }

    public WorkerSalaryHistory(
        Guid id,
        Guid workerId,
        decimal amount,
        string currencyCode,
        DateTime effectiveFrom,
        DateTime? effectiveTo = null,
        string? notes = null,
        Guid? createdBy = null,
        Guid? updatedBy = null,
        DateTime? createdAtUtc = null)
    {
        if (workerId == Guid.Empty)
            throw new ArgumentException("WorkerId is required.", nameof(workerId));
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than or equal to 0.");
        if (string.IsNullOrWhiteSpace(currencyCode))
            throw new ArgumentException("CurrencyCode is required.", nameof(currencyCode));
        if (effectiveFrom == default)
            throw new ArgumentException("EffectiveFrom is required.", nameof(effectiveFrom));
        if (effectiveTo.HasValue && effectiveTo.Value <= effectiveFrom)
            throw new ArgumentException("EffectiveTo must be after EffectiveFrom.");

        Id = id;
        WorkerId = workerId;
        Amount = amount;
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CreatedBy = createdBy;
        UpdatedBy = updatedBy;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; init; }
    public Guid WorkerId { get; init; }
    public Worker? Worker { get; set; }
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public DateTime EffectiveFrom { get; private set; }
    public DateTime? EffectiveTo { get; private set; }
    public string? Notes { get; private set; }
    public Guid? CreatedBy { get; init; }
    public Guid? UpdatedBy { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void UpdateCorrection(
        decimal amount,
        string currencyCode,
        DateTime effectiveFrom,
        DateTime? effectiveTo = null,
        string? notes = null,
        Guid? updatedBy = null,
        DateTime? atUtc = null)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than or equal to 0.");
        if (string.IsNullOrWhiteSpace(currencyCode))
            throw new ArgumentException("CurrencyCode is required.", nameof(currencyCode));
        if (effectiveFrom == default)
            throw new ArgumentException("EffectiveFrom is required.", nameof(effectiveFrom));
        if (effectiveTo.HasValue && effectiveTo.Value <= effectiveFrom)
            throw new ArgumentException("EffectiveTo must be after EffectiveFrom.");

        Amount = amount;
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedBy = updatedBy;
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }

    public void Close(DateTime effectiveTo, Guid? updatedBy = null, DateTime? atUtc = null)
    {
        if (effectiveTo <= EffectiveFrom)
            throw new ArgumentException("EffectiveTo must be after EffectiveFrom.");

        EffectiveTo = effectiveTo;
        UpdatedBy = updatedBy;
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }
}
