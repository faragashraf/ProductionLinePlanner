using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Entities;

public class MainStage
{
    public MainStage(
        Guid id,
        Guid productionLineId,
        string name,
        int sequenceOrder,
        bool isCritical = false,
        bool isActive = true,
        DateTime? createdAtUtc = null)
    {
        if (productionLineId == Guid.Empty)
            throw new ArgumentException("ProductionLineId is required.", nameof(productionLineId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Main stage name is required.", nameof(name));
        if (sequenceOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(sequenceOrder), "SequenceOrder must be zero or positive.");

        Id = id;
        ProductionLineId = productionLineId;
        Name = name.Trim();
        SequenceOrder = sequenceOrder;
        IsCritical = isCritical;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; init; }
    public Guid ProductionLineId { get; init; }
    public ProductionLine? ProductionLine { get; set; }
    public string Name { get; private set; }
    public int SequenceOrder { get; private set; }
    public bool IsCritical { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public List<SubStage> SubStages { get; } = [];

    public void Rename(string name, DateTime? atUtc = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Main stage name is required.", nameof(name));

        Name = name.Trim();
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }
}
