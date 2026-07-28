namespace ProductionLinePlanner.Domain.Entities;

public class SubStage
{
    private SubStage() { }

    public SubStage(
        Guid id,
        Guid mainStageId,
        string name,
        string code,
        int capacity,
        int defaultOrder,
        bool isActive = true,
        DateTime? createdAtUtc = null,
        Guid departmentId = default)
    {
        if (mainStageId == Guid.Empty)
            throw new ArgumentException("MainStageId is required.", nameof(mainStageId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Sub stage name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code is required.", nameof(code));
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be zero or positive.");
        if (defaultOrder <= 0)
            throw new ArgumentOutOfRangeException(nameof(defaultOrder), "DefaultOrder must be greater than zero.");

        Id = id;
        MainStageId = mainStageId;
        DepartmentId = departmentId;
        Name = name.Trim();
        Code = code.Trim();
        Capacity = capacity;
        DefaultOrder = defaultOrder;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; init; }
    public Guid MainStageId { get; init; }
    public MainStage? MainStage { get; set; }
    /// <summary>
    /// Integrity key used to enforce department-scoped stage-code uniqueness and
    /// a composite FK to MainStage. Ownership is still navigated through MainStage.
    /// </summary>
    public Guid DepartmentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public int Capacity { get; private set; }
    public int DefaultOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public int SequenceOrder
    {
        get => DefaultOrder;
        private set => DefaultOrder = value;
    }

    public List<WorkerDefaultAssignment> DefaultAssignments { get; } = [];

    /// <summary>
    /// Binds the integrity key to the owning MainStage department. The composite
    /// database FK prevents this value from diverging from MainStage.DepartmentId.
    /// </summary>
    public void SetDepartment(Guid departmentId)
    {
        if (departmentId == Guid.Empty)
            throw new ArgumentException("DepartmentId is required.", nameof(departmentId));
        if (DepartmentId != Guid.Empty && DepartmentId != departmentId)
            throw new InvalidOperationException("A stage cannot be moved to a different department.");

        DepartmentId = departmentId;
    }

    public void UpdateCapacity(int capacity, DateTime? atUtc = null)
    {
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be zero or positive.");

        Capacity = capacity;
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }

    public void Rename(string name, DateTime? atUtc = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Sub stage name is required.", nameof(name));

        Name = name.Trim();
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }

    public void SetOrder(int defaultOrder, DateTime? atUtc = null)
    {
        if (defaultOrder <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultOrder), "DefaultOrder must be greater than zero.");
        }

        DefaultOrder = defaultOrder;
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }
}
