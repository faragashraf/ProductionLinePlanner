using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

public sealed class DepartmentCatalogService(
    AppDbContext dbContext,
    IAuditEngine auditEngine) : IDepartmentCatalogService
{
    public async Task<PagedResult<DepartmentDto>> GetDepartmentsAsync(Guid? factoryId, string? search, bool? isActive, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 200)
        {
            return PagedResult<DepartmentDto>.Failure(new Error("ValidationError", "page and pageSize must be positive, pageSize max 200."));
        }

        var query = dbContext.Departments.AsNoTracking().AsQueryable();
        if (factoryId.HasValue) query = query.Where(x => x.FactoryId == factoryId.Value);
        if (isActive.HasValue) query = query.Where(x => x.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(x => EF.Functions.Like(x.Code, pattern) || EF.Functions.Like(x.NameAr, pattern) || (x.NameEn != null && EF.Functions.Like(x.NameEn, pattern)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.SequenceOrder).ThenBy(x => x.NameAr)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new DepartmentDto
            {
                Id = x.Id, FactoryId = x.FactoryId, Code = x.Code, NameAr = x.NameAr, NameEn = x.NameEn,
                SequenceOrder = x.SequenceOrder, IsActive = x.IsActive, ProductionLineCount = x.ProductionLines.Count
            }).ToArrayAsync(cancellationToken);
        return PagedResult<DepartmentDto>.Success(items, page, pageSize, total);
    }

    public async Task<Result<DepartmentDto>> GetDepartmentAsync(Guid departmentId, CancellationToken cancellationToken = default)
    {
        if (departmentId == Guid.Empty) return Result<DepartmentDto>.Failure(new Error("ValidationError", "DepartmentId is required."));
        var item = await DepartmentQuery().FirstOrDefaultAsync(x => x.Id == departmentId, cancellationToken);
        return item is null
            ? Result<DepartmentDto>.Failure(new Error("NotFound", "Department not found."))
            : Result<DepartmentDto>.Success(item);
    }

    public async Task<Result<DepartmentDto>> CreateAsync(Guid factoryId, string code, string nameAr, string? nameEn, int sequenceOrder, bool isActive, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty) return Result<DepartmentDto>.Failure(new Error("Unauthorized", "User context is required."));
        if (factoryId == Guid.Empty) return Result<DepartmentDto>.Failure(new Error("ValidationError", "FactoryId is required."));
        var normalizedCode = code?.Trim();
        var normalizedName = nameAr?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedCode) || string.IsNullOrWhiteSpace(normalizedName)) return Result<DepartmentDto>.Failure(new Error("ValidationError", "Code and NameAr are required."));
        if (sequenceOrder < 0) return Result<DepartmentDto>.Failure(new Error("ValidationError", "SequenceOrder must be zero or greater."));
        if (!await dbContext.Factories.AnyAsync(x => x.Id == factoryId && x.IsActive, cancellationToken)) return Result<DepartmentDto>.Failure(new Error("NotFound", "Factory was not found or is inactive."));
        if (await HasCodeConflictAsync(factoryId, normalizedCode, null, cancellationToken)) return Result<DepartmentDto>.Failure(new Error("Conflict", "Department code must be unique within the factory."));

        var entity = new Department(Guid.NewGuid(), factoryId, normalizedCode, normalizedName, nameEn, sequenceOrder, isActive);
        dbContext.Departments.Add(entity);
        await auditEngine.RecordAsync(actorUserId, AuditActionType.Create, nameof(Department), entity.Id.ToString(), after: DepartmentAudit(entity), requestMeta: requestMeta, cancellationToken: cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result<DepartmentDto>.Success(ToDto(entity, 0));
    }

    public async Task<Result<DepartmentDto>> UpdateAsync(Guid departmentId, string? code, string? nameAr, string? nameEn, int? sequenceOrder, bool? isActive, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty) return Result<DepartmentDto>.Failure(new Error("Unauthorized", "User context is required."));
        if (departmentId == Guid.Empty) return Result<DepartmentDto>.Failure(new Error("ValidationError", "DepartmentId is required."));
        var entity = await dbContext.Departments.FirstOrDefaultAsync(x => x.Id == departmentId, cancellationToken);
        if (entity is null) return Result<DepartmentDto>.Failure(new Error("NotFound", "Department not found."));
        if (code is null && nameAr is null && nameEn is null && sequenceOrder is null && isActive is null) return Result<DepartmentDto>.Failure(new Error("ValidationError", "No updatable fields were provided."));

        var normalizedCode = code?.Trim();
        var normalizedName = nameAr?.Trim();
        if (normalizedCode is not null && string.IsNullOrWhiteSpace(normalizedCode)) return Result<DepartmentDto>.Failure(new Error("ValidationError", "Code cannot be empty."));
        if (normalizedName is not null && string.IsNullOrWhiteSpace(normalizedName)) return Result<DepartmentDto>.Failure(new Error("ValidationError", "NameAr cannot be empty."));
        if (sequenceOrder is < 0) return Result<DepartmentDto>.Failure(new Error("ValidationError", "SequenceOrder must be zero or greater."));
        if (normalizedCode is not null && !string.Equals(normalizedCode, entity.Code, StringComparison.OrdinalIgnoreCase) && await HasCodeConflictAsync(entity.FactoryId, normalizedCode, entity.Id, cancellationToken)) return Result<DepartmentDto>.Failure(new Error("Conflict", "Department code must be unique within the factory."));
        if (isActive is false && entity.IsActive && await dbContext.ProductionLines.AnyAsync(x => x.DepartmentId == entity.Id && x.IsActive, cancellationToken)) return Result<DepartmentDto>.Failure(new Error("Conflict", "لا يمكن تعطيل القسم لوجود خطوط إنتاج نشطة مرتبطة به."));

        var before = DepartmentAudit(entity);
        if (normalizedCode is not null || normalizedName is not null || nameEn is not null || sequenceOrder is not null)
        {
            entity.Update(normalizedCode ?? entity.Code, normalizedName ?? entity.NameAr, nameEn ?? entity.NameEn, sequenceOrder ?? entity.SequenceOrder);
        }
        if (isActive is not null && isActive.Value != entity.IsActive)
        {
            if (isActive.Value) entity.Activate(); else entity.Deactivate();
        }
        await auditEngine.RecordAsync(actorUserId, AuditActionType.Update, nameof(Department), entity.Id.ToString(), before, DepartmentAudit(entity), requestMeta, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        var lineCount = await dbContext.ProductionLines.CountAsync(x => x.DepartmentId == entity.Id, cancellationToken);
        return Result<DepartmentDto>.Success(ToDto(entity, lineCount));
    }

    public async Task<Result> DeleteAsync(Guid departmentId, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty) return Result.Failure(new Error("Unauthorized", "User context is required."));
        var entity = await dbContext.Departments.FirstOrDefaultAsync(x => x.Id == departmentId, cancellationToken);
        if (entity is null) return Result.Failure(new Error("NotFound", "Department not found."));
        if (await dbContext.ProductionLines.AnyAsync(x => x.DepartmentId == departmentId, cancellationToken)) return Result.Failure(new Error("Conflict", "لا يمكن حذف القسم لوجود خطوط إنتاج مرتبطة به."));
        await auditEngine.RecordAsync(actorUserId, AuditActionType.Delete, nameof(Department), entity.Id.ToString(), DepartmentAudit(entity), null, requestMeta, cancellationToken);
        dbContext.Departments.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private IQueryable<DepartmentDto> DepartmentQuery() => dbContext.Departments.AsNoTracking().Select(x => new DepartmentDto
    {
        Id = x.Id, FactoryId = x.FactoryId, Code = x.Code, NameAr = x.NameAr, NameEn = x.NameEn,
        SequenceOrder = x.SequenceOrder, IsActive = x.IsActive, ProductionLineCount = x.ProductionLines.Count
    });

    private Task<bool> HasCodeConflictAsync(Guid factoryId, string code, Guid? exceptId, CancellationToken cancellationToken) =>
        dbContext.Departments.AnyAsync(x => x.FactoryId == factoryId && x.Code.ToUpper() == code.ToUpper() && (!exceptId.HasValue || x.Id != exceptId.Value), cancellationToken);

    private static DepartmentDto ToDto(Department entity, int lineCount) => new()
    {
        Id = entity.Id, FactoryId = entity.FactoryId, Code = entity.Code, NameAr = entity.NameAr, NameEn = entity.NameEn,
        SequenceOrder = entity.SequenceOrder, IsActive = entity.IsActive, ProductionLineCount = lineCount
    };

    private static object DepartmentAudit(Department entity) => new { entity.Id, entity.FactoryId, entity.Code, entity.NameAr, entity.NameEn, entity.SequenceOrder, entity.IsActive };
}
