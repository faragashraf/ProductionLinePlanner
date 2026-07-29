using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Services;

public interface IDepartmentCatalogService
{
    Task<PagedResult<DepartmentDto>> GetDepartmentsAsync(Guid? factoryId, string? search, bool? isActive, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<Result<DepartmentDto>> GetDepartmentAsync(Guid departmentId, CancellationToken cancellationToken = default);
    Task<Result<DepartmentDto>> CreateAsync(Guid factoryId, string code, string nameAr, string? nameEn, int sequenceOrder, bool isActive, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default);
    Task<Result<DepartmentDto>> UpdateAsync(Guid departmentId, string? code, string? nameAr, string? nameEn, int? sequenceOrder, bool? isActive, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid departmentId, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default);
}
