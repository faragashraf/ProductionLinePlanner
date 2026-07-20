namespace ProductionLinePlanner.Application.Requests;

public sealed class UpdateProductionLineRequest
{
    public string? Name { get; init; }
    public Guid? DepartmentId { get; init; }
    public string? LineCode { get; init; }
    public int? SequenceOrder { get; init; }
    public bool? IsActive { get; init; }
}
