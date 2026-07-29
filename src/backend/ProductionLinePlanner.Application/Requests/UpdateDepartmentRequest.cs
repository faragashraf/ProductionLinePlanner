namespace ProductionLinePlanner.Application.Requests;

public sealed class UpdateDepartmentRequest
{
    public string? Code { get; init; }
    public string? NameAr { get; init; }
    public string? NameEn { get; init; }
    public int? SequenceOrder { get; init; }
    public bool? IsActive { get; init; }
}
