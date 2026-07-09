namespace ProductionLinePlanner.Application.Abstractions;

public interface ICurrentUserService
{
    Guid? UserId { get; }

    string? UserName { get; }

    bool IsAuthenticated { get; }

    IReadOnlyCollection<string> Roles { get; }
}
