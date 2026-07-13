namespace ProductionLinePlanner.Application.Requests;

public sealed class UpdateProductModelRequest
{
    public string? Code { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public bool? IsActive { get; init; }
}
