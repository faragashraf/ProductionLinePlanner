namespace ProductionLinePlanner.Domain.Entities;

/// <summary>
/// Local operational department. This is intentionally separate from the
/// attendance-source department reference stored on workers.
/// </summary>
public class Department
{
    private Department() { }

    public Department(
        Guid id,
        Guid factoryId,
        string code,
        string nameAr,
        string? nameEn,
        int sequenceOrder,
        bool isActive = true,
        DateTime? createdAtUtc = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Department Id is required.", nameof(id));
        if (factoryId == Guid.Empty) throw new ArgumentException("FactoryId is required.", nameof(factoryId));
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Department code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(nameAr)) throw new ArgumentException("Arabic department name is required.", nameof(nameAr));
        if (sequenceOrder < 0) throw new ArgumentOutOfRangeException(nameof(sequenceOrder), "SequenceOrder must be zero or positive.");

        Id = id;
        FactoryId = factoryId;
        Code = code.Trim();
        NameAr = nameAr.Trim();
        NameEn = string.IsNullOrWhiteSpace(nameEn) ? null : nameEn.Trim();
        SequenceOrder = sequenceOrder;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; init; }
    public Guid FactoryId { get; init; }
    public Factory? Factory { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string? NameEn { get; private set; }
    public int SequenceOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public List<ProductionLine> ProductionLines { get; } = [];

    public void Update(string nameAr, string? nameEn, int sequenceOrder, DateTime? atUtc = null)
    {
        if (string.IsNullOrWhiteSpace(nameAr)) throw new ArgumentException("Arabic department name is required.", nameof(nameAr));
        if (sequenceOrder < 0) throw new ArgumentOutOfRangeException(nameof(sequenceOrder), "SequenceOrder must be zero or positive.");

        NameAr = nameAr.Trim();
        NameEn = string.IsNullOrWhiteSpace(nameEn) ? null : nameEn.Trim();
        SequenceOrder = sequenceOrder;
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }

    public void Activate(DateTime? atUtc = null)
    {
        IsActive = true;
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }

    public void Deactivate(DateTime? atUtc = null)
    {
        IsActive = false;
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }
}
