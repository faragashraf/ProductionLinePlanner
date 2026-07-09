namespace ProductionLinePlanner.Application.Requests;

public sealed class ResetSuperAdminPasswordRequest
{
    public string BootstrapSecret { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
}
