namespace ProductionLinePlanner.Application.Requests;

public sealed class CreateMainStageRequest
{
    public Guid DepartmentId { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsCritical { get; init; }
    public int SequenceOrder { get; init; }
    public bool IsActive { get; init; } = true;
}
