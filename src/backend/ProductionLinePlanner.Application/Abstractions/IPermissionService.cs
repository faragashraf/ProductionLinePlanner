using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Abstractions;

public interface IPermissionService
{
    Task<IReadOnlyCollection<string>> GetEffectivePermissionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<PermissionCatalogItemDto[]> GetCatalogAsync(
        CancellationToken cancellationToken = default);
}
