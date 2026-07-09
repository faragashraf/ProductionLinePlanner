namespace ProductionLinePlanner.Application.Requests;

public sealed class LogoutRequest
{
    public string RefreshToken { get; init; } = string.Empty;
}
