namespace ProductionLinePlanner.Domain.Entities;

public class SubStage
{
    private SubStage() { }

    public SubStage(
        Guid id,
        Guid mainStageId,
        string name,
        int capacity,
        int sequenceOrder,
        bool isActive = true,
        DateTime? createdAtUtc = null)
    {
        if (mainStageId == Guid.Empty)
            throw new ArgumentException("MainStageId is required.", nameof(mainStageId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Sub stage name is required.", nameof(name));
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be zero or positive.");
        if (sequenceOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(sequenceOrder), "SequenceOrder must be zero or positive.");

        Id = id;
        MainStageId = mainStageId;
        Name = name.Trim();
        Capacity = capacity;
        SequenceOrder = sequenceOrder;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; init; }
    public Guid MainStageId { get; init; }
    public MainStage? MainStage { get; set; }
    public string Name { get; private set; } = string.Empty;
    public int Capacity { get; private set; }
    public int SequenceOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public List<WorkerDefaultAssignment> DefaultAssignments { get; } = [];

    public void UpdateCapacity(int capacity, DateTime? atUtc = null)
    {
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be zero or positive.");

        Capacity = capacity;
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }
}
