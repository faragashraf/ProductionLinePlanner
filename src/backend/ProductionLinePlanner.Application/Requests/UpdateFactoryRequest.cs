namespace ProductionLinePlanner.Application.Requests;

public sealed class UpdateFactoryRequest
{
    public string? Name { get; init; }
    public string? Location { get; init; }
    public bool? IsActive { get; init; }
}
