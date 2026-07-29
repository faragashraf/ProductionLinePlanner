using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Entities;

public class MainStage
{
    private MainStage() { }

    public MainStage(
        Guid id,
        Guid departmentId,
        string name,
        int sequenceOrder,
        bool isCritical = false,
        bool isActive = true,
        DateTime? createdAtUtc = null)
    {
        if (departmentId == Guid.Empty)
            throw new ArgumentException("DepartmentId is required.", nameof(departmentId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Main stage name is required.", nameof(name));
        if (sequenceOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(sequenceOrder), "SequenceOrder must be zero or positive.");

        Id = id;
        DepartmentId = departmentId;
        Name = name.Trim();
        SequenceOrder = sequenceOrder;
        IsCritical = isCritical;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; init; }
    public Guid DepartmentId { get; init; }
    public Department? Department { get; set; }
    public string Name { get; private set; } = string.Empty;
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
