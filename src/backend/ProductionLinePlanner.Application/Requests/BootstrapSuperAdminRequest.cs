namespace ProductionLinePlanner.Application.Requests;

public sealed class BootstrapSuperAdminRequest
{
    public string BootstrapSecret { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
