namespace ProductionLinePlanner.Application.Requests;

public sealed class RefreshTokenRequest
{
    public string RefreshToken { get; init; } = string.Empty;
}
