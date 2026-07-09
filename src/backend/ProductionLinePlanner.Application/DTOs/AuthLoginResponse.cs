namespace ProductionLinePlanner.Application.DTOs;

public sealed class AuthLoginResponse
{
    public string AccessToken { get; init; } = string.Empty;
    // Deferred: refresh token issuance is intentionally disabled until backend refresh flow is implemented.
    public string? RefreshToken { get; init; }
    public DateTime ExpiresAt { get; init; }
    public Guid UserId { get; init; }
    public string[] Roles { get; init; } = [];
    public string[] Permissions { get; init; } = [];
}
