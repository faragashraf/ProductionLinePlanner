namespace ProductionLinePlanner.Application.Requests;

public sealed class CreateFactoryRequest
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Location { get; init; }
    public bool IsActive { get; init; } = true;
}
