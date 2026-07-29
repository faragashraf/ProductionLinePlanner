namespace ProductionLinePlanner.Application.Realtime;

public interface ICapabilityGroupResolver
{
    Task<IReadOnlyCollection<string>> ResolveGroupsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    string GetGroupName(string permission);
}
