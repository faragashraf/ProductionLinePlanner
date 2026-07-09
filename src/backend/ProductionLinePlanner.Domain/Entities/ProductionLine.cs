using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Entities;

public class ProductionLine
{
    private ProductionLine() { }

    public ProductionLine(
        Guid id,
        Guid factoryId,
        string name,
        int sequenceOrder,
        string? lineCode = null,
        bool isActive = true,
        DateTime? createdAtUtc = null)
    {
        if (factoryId == Guid.Empty)
            throw new ArgumentException("FactoryId is required.", nameof(factoryId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Production line name is required.", nameof(name));
        if (sequenceOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(sequenceOrder), "SequenceOrder must be zero or positive.");

        Id = id;
        FactoryId = factoryId;
        Name = name.Trim();
        SequenceOrder = sequenceOrder;
        LineCode = string.IsNullOrWhiteSpace(lineCode) ? null : lineCode.Trim();
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; init; }
    public Guid FactoryId { get; init; }
    public Factory? Factory { get; set; }
    public string Name { get; private set; }
    public string? LineCode { get; private set; }
    public int SequenceOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public List<MainStage> MainStages { get; } = [];

    public void SetSequenceOrder(int sequenceOrder, DateTime? atUtc = null)
    {
        if (sequenceOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(sequenceOrder), "SequenceOrder must be zero or positive.");
        SequenceOrder = sequenceOrder;
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }

    public void Activate(DateTime? atUtc = null) => IsActive = true;
    public void Deactivate(DateTime? atUtc = null) => IsActive = false;
}
