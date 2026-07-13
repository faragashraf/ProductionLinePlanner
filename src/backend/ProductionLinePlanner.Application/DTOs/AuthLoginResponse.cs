namespace ProductionLinePlanner.Application.DTOs;

public sealed class AuthLoginResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public Guid UserId { get; init; }
    public bool IsActive { get; init; }
    public string[] Roles { get; init; } = [];
    public string[] Permissions { get; init; } = [];
    public DateTime PermissionsVersion { get; init; }
}
