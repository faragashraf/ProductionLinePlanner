namespace ProductionLinePlanner.Application.DTOs;

public sealed class DepartmentDto
{
    public Guid Id { get; init; }
    public Guid FactoryId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameAr { get; init; } = string.Empty;
    public string? NameEn { get; init; }
    public int SequenceOrder { get; init; }
    public bool IsActive { get; init; }
    public int ProductionLineCount { get; init; }
}
