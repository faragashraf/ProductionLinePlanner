namespace ProductionLinePlanner.Application.DTOs;

public sealed class SubStageDto
{
    public Guid Id { get; init; }
    public Guid MainStageId { get; init; }
    public Guid ProductionLineId { get; init; }
    public Guid FactoryId { get; init; }
    public Guid? DepartmentId { get; init; }
    public string? FactoryName { get; init; }
    public string? DepartmentNameAr { get; init; }
    public string? ProductionLineName { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Capacity { get; init; }
    public int DefaultOrder { get; init; }
    public bool IsActive { get; init; }
}
