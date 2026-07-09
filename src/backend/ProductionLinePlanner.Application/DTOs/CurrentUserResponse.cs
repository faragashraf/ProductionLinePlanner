namespace ProductionLinePlanner.Application.DTOs;

public sealed class CurrentUserResponse
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string[] Roles { get; init; } = [];
    public string[] Permissions { get; init; } = [];
}
