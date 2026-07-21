namespace ProductionLinePlanner.Application.Requests;

public sealed class UpdateFactoryRequest
{
    // Retained solely to reject legacy/manual attempts to change an immutable code.
    public string? Code { get; init; }
    public string? Name { get; init; }
    public string? Location { get; init; }
    public bool? IsActive { get; init; }
}
