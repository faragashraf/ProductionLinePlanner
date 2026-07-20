namespace ProductionLinePlanner.Application.Requests;

/// <summary>Creates the operational stage represented by <see cref="Domain.Entities.SubStage"/>.</summary>
public sealed class CreateOperationalStageRequest
{
    public Guid ProductionLineId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Capacity { get; init; }
    public bool IsActive { get; init; } = true;
}
