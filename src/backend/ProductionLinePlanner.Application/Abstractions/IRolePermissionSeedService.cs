using System.Collections.Generic;

namespace ProductionLinePlanner.Application.Abstractions;

public interface IRolePermissionSeedService
{
    Task EnsureSeedAsync(CancellationToken cancellationToken = default);
}
