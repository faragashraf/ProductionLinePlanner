using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

public sealed class ProductionStageCatalogService(
    AppDbContext dbContext,
    IAuditEngine auditEngine) : IProductionStageCatalogService
{
    public async Task<PagedResult<MainStageDto>> GetMainStagesAsync(
        Guid? productionLineId,
        string? search,
        bool? isActive = true,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 200)
        {
            return PagedResult<MainStageDto>.Failure(new Error("ValidationError", "page and pageSize must be positive, pageSize max 200."));
        }

        var query = dbContext.MainStages.AsNoTracking().AsQueryable();
        if (productionLineId.HasValue)
        {
            query = query.Where(x => x.ProductionLineId == productionLineId.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(x => x.IsActive == isActive.Value);
        }

        var searchTerm = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        if (searchTerm is not null)
        {
            query = query.Where(x => EF.Functions.Like(x.Name, searchTerm));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(x => x.SequenceOrder)
            .ThenBy(x => x.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new MainStageDto
            {
                Id = x.Id,
                ProductionLineId = x.ProductionLineId,
                Name = x.Name,
                SequenceOrder = x.SequenceOrder,
                IsCritical = x.IsCritical,
                IsActive = x.IsActive
            })
            .ToArrayAsync(cancellationToken);

        return PagedResult<MainStageDto>.Success(rows, page, pageSize, total);
    }

    public async Task<Result<MainStageDto>> GetMainStageAsync(Guid mainStageId, CancellationToken cancellationToken = default)
    {
        if (mainStageId == Guid.Empty)
        {
            return Result<MainStageDto>.Failure(new Error("ValidationError", "MainStageId is required."));
        }

        var entity = await dbContext.MainStages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == mainStageId, cancellationToken);
        if (entity is null)
        {
            return Result<MainStageDto>.Failure(new Error("NotFound", "Main stage not found."));
        }

        return Result<MainStageDto>.Success(new MainStageDto
        {
            Id = entity.Id,
            ProductionLineId = entity.ProductionLineId,
            Name = entity.Name,
            SequenceOrder = entity.SequenceOrder,
            IsCritical = entity.IsCritical,
            IsActive = entity.IsActive
        });
    }

    public async Task<PagedResult<SubStageDto>> GetSubStagesAsync(
        Guid? mainStageId,
        string? search,
        bool? isActive = true,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 200)
        {
            return PagedResult<SubStageDto>.Failure(new Error("ValidationError", "page and pageSize must be positive, pageSize max 200."));
        }

        var query = dbContext.SubStages.AsNoTracking().AsQueryable();
        if (mainStageId.HasValue)
        {
            query = query.Where(x => x.MainStageId == mainStageId.Value);
        }

        var searchTerm = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        if (searchTerm is not null)
        {
            query = query.Where(x => EF.Functions.Like(x.Code, searchTerm) || EF.Functions.Like(x.Name, searchTerm));
        }

        if (isActive.HasValue)
        {
            query = query.Where(x => x.IsActive == isActive.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(x => x.DefaultOrder)
            .ThenBy(x => x.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new SubStageDto
            {
                Id = x.Id,
                MainStageId = x.MainStageId,
                Code = x.Code,
                Name = x.Name,
                Capacity = x.Capacity,
                DefaultOrder = x.DefaultOrder,
                IsActive = x.IsActive
            })
            .ToArrayAsync(cancellationToken);

        return PagedResult<SubStageDto>.Success(rows, page, pageSize, total);
    }

    public async Task<Result<SubStageDto>> GetSubStageAsync(Guid subStageId, CancellationToken cancellationToken = default)
    {
        if (subStageId == Guid.Empty)
        {
            return Result<SubStageDto>.Failure(new Error("ValidationError", "SubStageId is required."));
        }

        var entity = await dbContext.SubStages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == subStageId, cancellationToken);
        if (entity is null)
        {
            return Result<SubStageDto>.Failure(new Error("NotFound", "Sub stage not found."));
        }

        return Result<SubStageDto>.Success(new SubStageDto
        {
            Id = entity.Id,
            MainStageId = entity.MainStageId,
            Code = entity.Code,
            Name = entity.Name,
            Capacity = entity.Capacity,
            DefaultOrder = entity.DefaultOrder,
            IsActive = entity.IsActive
        });
    }

    public async Task<Result<MainStageDto>> CreateMainStageAsync(
        Guid productionLineId,
        string name,
        bool isCritical,
        int sequenceOrder,
        bool isActive,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<MainStageDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (productionLineId == Guid.Empty)
        {
            return Result<MainStageDto>.Failure(new Error("ValidationError", "ProductionLineId is required."));
        }

        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return Result<MainStageDto>.Failure(new Error("ValidationError", "Name is required."));
        }

        if (sequenceOrder < 0)
        {
            return Result<MainStageDto>.Failure(new Error("ValidationError", "SequenceOrder must be zero or greater."));
        }

        var lineExists = await dbContext.ProductionLines.AnyAsync(x => x.Id == productionLineId && x.IsActive, cancellationToken);
        if (!lineExists)
        {
            return Result<MainStageDto>.Failure(new Error("NotFound", "ProductionLine was not found."));
        }

        var conflict = await dbContext.MainStages.AnyAsync(
            x => x.ProductionLineId == productionLineId && x.SequenceOrder == sequenceOrder && x.IsActive,
            cancellationToken);
        if (conflict)
        {
            return Result<MainStageDto>.Failure(new Error("Conflict", "SequenceOrder must be unique for this production line."));
        }

        var entity = new MainStage(
            id: Guid.NewGuid(),
            productionLineId: productionLineId,
            name: normalizedName,
            isCritical: isCritical,
            sequenceOrder: sequenceOrder,
            isActive: isActive);

        dbContext.MainStages.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Create,
            nameof(MainStage),
            entity.Id.ToString(),
            before: null,
            after: new { entity.Id, entity.ProductionLineId, entity.Name, entity.IsCritical, entity.SequenceOrder, entity.IsActive },
            requestMeta: requestMeta);

        return Result<MainStageDto>.Success(new MainStageDto
        {
            Id = entity.Id,
            ProductionLineId = entity.ProductionLineId,
            Name = entity.Name,
            SequenceOrder = entity.SequenceOrder,
            IsCritical = entity.IsCritical,
            IsActive = entity.IsActive
        });
    }

    public async Task<Result<MainStageDto>> UpdateMainStageAsync(
        Guid mainStageId,
        string? name,
        bool? isCritical,
        int? sequenceOrder,
        bool? isActive,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<MainStageDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (mainStageId == Guid.Empty)
        {
            return Result<MainStageDto>.Failure(new Error("ValidationError", "MainStageId is required."));
        }

        var entity = await dbContext.MainStages.FirstOrDefaultAsync(x => x.Id == mainStageId, cancellationToken);
        if (entity is null)
        {
            return Result<MainStageDto>.Failure(new Error("NotFound", "Main stage not found."));
        }

        if (name is null && isCritical is null && sequenceOrder is null && isActive is null)
        {
            return Result<MainStageDto>.Failure(new Error("ValidationError", "No updatable fields were provided."));
        }

        var normalizedName = name?.Trim();
        if (normalizedName is not null && string.IsNullOrWhiteSpace(normalizedName))
        {
            return Result<MainStageDto>.Failure(new Error("ValidationError", "Name cannot be empty."));
        }

        var duplicateSequence = false;
        if (sequenceOrder is not null && sequenceOrder.Value < 0)
        {
            return Result<MainStageDto>.Failure(new Error("ValidationError", "SequenceOrder must be zero or greater."));
        }

        if (sequenceOrder is not null && entity.SequenceOrder != sequenceOrder.Value)
        {
            duplicateSequence = await dbContext.MainStages.AnyAsync(
                x => x.Id != entity.Id && x.ProductionLineId == entity.ProductionLineId && x.SequenceOrder == sequenceOrder.Value && x.IsActive,
                cancellationToken);
            if (duplicateSequence)
            {
                return Result<MainStageDto>.Failure(new Error("Conflict", "SequenceOrder must be unique for this production line."));
            }
        }

        var before = new { entity.Id, entity.ProductionLineId, entity.Name, entity.IsCritical, entity.SequenceOrder, entity.IsActive };
        if (normalizedName is not null)
        {
            entity.Rename(normalizedName);
            dbContext.Entry(entity).Property(nameof(MainStage.SequenceOrder)).CurrentValue = entity.SequenceOrder;
        }

        if (isCritical is not null)
        {
            dbContext.Entry(entity).Property(nameof(MainStage.IsCritical)).CurrentValue = isCritical.Value;
        }

        if (sequenceOrder is not null)
        {
            dbContext.Entry(entity).Property(nameof(MainStage.SequenceOrder)).CurrentValue = sequenceOrder.Value;
        }

        if (isActive is not null)
        {
            dbContext.Entry(entity).Property(nameof(MainStage.IsActive)).CurrentValue = isActive.Value;
        }

        dbContext.Entry(entity).Property(nameof(MainStage.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Update,
            nameof(MainStage),
            entity.Id.ToString(),
            before,
            new { entity.Id, entity.ProductionLineId, entity.Name, entity.IsCritical, entity.SequenceOrder, entity.IsActive },
            requestMeta);

        return Result<MainStageDto>.Success(new MainStageDto
        {
            Id = entity.Id,
            ProductionLineId = entity.ProductionLineId,
            Name = entity.Name,
            SequenceOrder = entity.SequenceOrder,
            IsCritical = entity.IsCritical,
            IsActive = entity.IsActive
        });
    }

    public async Task<Result> DeactivateMainStageAsync(
        Guid mainStageId,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (mainStageId == Guid.Empty)
        {
            return Result.Failure(new Error("ValidationError", "MainStageId is required."));
        }

        var entity = await dbContext.MainStages.FirstOrDefaultAsync(x => x.Id == mainStageId && x.IsActive, cancellationToken);
        if (entity is null)
        {
            return Result.Failure(new Error("NotFound", "Main stage not found."));
        }

        var hasAssignments = await dbContext.WorkerDefaultAssignments
            .Include(x => x.SubStage)
            .AsNoTracking()
            .AnyAsync(x => x.IsActive && x.SubStage != null && x.SubStage.MainStageId == entity.Id, cancellationToken);
        if (hasAssignments)
        {
            return Result.Failure(new Error("Conflict", "Main stage cannot be deactivated while it has active assignments."));
        }

        var before = new { entity.Id, entity.ProductionLineId, entity.Name, entity.IsCritical, entity.SequenceOrder, entity.IsActive };
        dbContext.Entry(entity).Property(nameof(MainStage.IsActive)).CurrentValue = false;
        dbContext.Entry(entity).Property(nameof(MainStage.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Update,
            nameof(MainStage),
            entity.Id.ToString(),
            before: before,
            after: new { entity.Id, entity.ProductionLineId, entity.Name, entity.IsCritical, entity.SequenceOrder, entity.IsActive },
            requestMeta: requestMeta);

        return Result.Success();
    }

    public async Task<Result<SubStageDto>> CreateSubStageAsync(
        Guid mainStageId,
        string code,
        string name,
        int defaultOrder,
        int capacity,
        bool isActive,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<SubStageDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (mainStageId == Guid.Empty)
        {
            return Result<SubStageDto>.Failure(new Error("ValidationError", "MainStageId is required."));
        }

        var normalizedCode = code?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return Result<SubStageDto>.Failure(new Error("ValidationError", "Code is required."));
        }

        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return Result<SubStageDto>.Failure(new Error("ValidationError", "Name is required."));
        }

        if (capacity < 0)
        {
            return Result<SubStageDto>.Failure(new Error("ValidationError", "Capacity must be zero or greater."));
        }

        if (defaultOrder <= 0)
        {
            return Result<SubStageDto>.Failure(new Error("ValidationError", "DefaultOrder must be greater than zero."));
        }

        var mainStageExists = await dbContext.MainStages.AnyAsync(x => x.Id == mainStageId && x.IsActive, cancellationToken);
        if (!mainStageExists)
        {
            return Result<SubStageDto>.Failure(new Error("NotFound", "Main stage was not found."));
        }

        var codeConflict = await dbContext.SubStages.AnyAsync(x => x.Code == normalizedCode, cancellationToken);
        if (codeConflict)
        {
            return Result<SubStageDto>.Failure(new Error("Conflict", "SubStage code must be unique."));
        }

        var orderConflict = await dbContext.SubStages.AnyAsync(
            x => x.MainStageId == mainStageId && x.DefaultOrder == defaultOrder && x.IsActive,
            cancellationToken);
        if (orderConflict)
        {
            return Result<SubStageDto>.Failure(new Error("Conflict", "DefaultOrder must be unique within this main stage."));
        }

        var entity = new SubStage(
            id: Guid.NewGuid(),
            mainStageId: mainStageId,
            name: normalizedName,
            code: normalizedCode,
            capacity: capacity,
            defaultOrder: defaultOrder,
            isActive: isActive);

        dbContext.SubStages.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Create,
            nameof(SubStage),
            entity.Id.ToString(),
            before: null,
            after: new { entity.Id, entity.MainStageId, entity.Code, entity.Name, entity.Capacity, entity.DefaultOrder, entity.IsActive },
            requestMeta: requestMeta);

        return Result<SubStageDto>.Success(new SubStageDto
        {
            Id = entity.Id,
            MainStageId = entity.MainStageId,
            Code = entity.Code,
            Name = entity.Name,
            Capacity = entity.Capacity,
            DefaultOrder = entity.DefaultOrder,
            IsActive = entity.IsActive
        });
    }

    public async Task<Result<SubStageDto>> UpdateSubStageAsync(
        Guid subStageId,
        string? code,
        string? name,
        int? defaultOrder,
        int? capacity,
        bool? isActive,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<SubStageDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (subStageId == Guid.Empty)
        {
            return Result<SubStageDto>.Failure(new Error("ValidationError", "SubStageId is required."));
        }

        var entity = await dbContext.SubStages.FirstOrDefaultAsync(x => x.Id == subStageId, cancellationToken);
        if (entity is null)
        {
            return Result<SubStageDto>.Failure(new Error("NotFound", "Sub stage not found."));
        }

        if (code is null && name is null && defaultOrder is null && capacity is null && isActive is null)
        {
            return Result<SubStageDto>.Failure(new Error("ValidationError", "No updatable fields were provided."));
        }

        var normalizedCode = code?.Trim();
        if (normalizedCode is not null && string.IsNullOrWhiteSpace(normalizedCode))
        {
            return Result<SubStageDto>.Failure(new Error("ValidationError", "Code cannot be empty."));
        }

        var normalizedName = name?.Trim();
        if (normalizedName is not null && string.IsNullOrWhiteSpace(normalizedName))
        {
            return Result<SubStageDto>.Failure(new Error("ValidationError", "Name cannot be empty."));
        }

        if (capacity is not null && capacity.Value < 0)
        {
            return Result<SubStageDto>.Failure(new Error("ValidationError", "Capacity must be zero or greater."));
        }

        if (defaultOrder is not null && defaultOrder.Value <= 0)
        {
            return Result<SubStageDto>.Failure(new Error("ValidationError", "DefaultOrder must be greater than zero."));
        }

        if (normalizedCode is not null)
        {
            var codeConflict = await dbContext.SubStages.AnyAsync(
                x => x.Id != subStageId && x.Code == normalizedCode,
                cancellationToken);
            if (codeConflict)
            {
                return Result<SubStageDto>.Failure(new Error("Conflict", "SubStage code must be unique."));
            }
        }

        if (defaultOrder is not null)
        {
            var orderConflict = await dbContext.SubStages.AnyAsync(
                x => x.Id != subStageId && x.MainStageId == entity.MainStageId && x.DefaultOrder == defaultOrder.Value && x.IsActive,
                cancellationToken);
            if (orderConflict)
            {
                return Result<SubStageDto>.Failure(new Error("Conflict", "DefaultOrder must be unique within this main stage."));
            }
        }

        var before = new
        {
            entity.Id,
            entity.MainStageId,
            entity.Code,
            entity.Name,
            entity.Capacity,
            entity.DefaultOrder,
            entity.IsActive
        };

        if (normalizedCode is not null || normalizedName is not null || defaultOrder is not null || capacity is not null)
        {
            entity.Rename(normalizedCode ?? entity.Code, normalizedName ?? entity.Name);
            if (capacity is not null)
            {
                entity.UpdateCapacity(capacity.Value);
            }

            if (defaultOrder is not null)
            {
                entity.SetOrder(defaultOrder.Value);
            }
        }

        if (isActive is not null)
        {
            dbContext.Entry(entity).Property(nameof(SubStage.IsActive)).CurrentValue = isActive.Value;
        }

        dbContext.Entry(entity).Property(nameof(SubStage.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Update,
            nameof(SubStage),
            entity.Id.ToString(),
            before: before,
            after: new { entity.Id, entity.MainStageId, entity.Code, entity.Name, entity.Capacity, entity.DefaultOrder, entity.IsActive },
            requestMeta: requestMeta);

        return Result<SubStageDto>.Success(new SubStageDto
        {
            Id = entity.Id,
            MainStageId = entity.MainStageId,
            Code = entity.Code,
            Name = entity.Name,
            Capacity = entity.Capacity,
            DefaultOrder = entity.DefaultOrder,
            IsActive = entity.IsActive
        });
    }

    public async Task<Result> DeactivateSubStageAsync(
        Guid subStageId,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (subStageId == Guid.Empty)
        {
            return Result.Failure(new Error("ValidationError", "SubStageId is required."));
        }

        var entity = await dbContext.SubStages.FirstOrDefaultAsync(x => x.Id == subStageId && x.IsActive, cancellationToken);
        if (entity is null)
        {
            return Result.Failure(new Error("NotFound", "Sub stage not found."));
        }

        var hasDefaultAssignments = await dbContext.WorkerDefaultAssignments.AnyAsync(x => x.IsActive && x.SubStageId == subStageId, cancellationToken);
        if (hasDefaultAssignments)
        {
            return Result.Failure(new Error("Conflict", "Sub stage cannot be deactivated while active default assignments exist."));
        }

        var hasActiveTemporaryAssignments = await dbContext.WorkerTemporaryAssignments.AnyAsync(
            x => (x.Status == "Active" || x.Status == "Scheduled") && x.ToSubStageId == subStageId,
            cancellationToken);
        if (hasActiveTemporaryAssignments)
        {
            return Result.Failure(new Error("Conflict", "Sub stage cannot be deactivated while active temporary assignments exist."));
        }

        var before = new { entity.Id, entity.MainStageId, entity.Code, entity.Name, entity.Capacity, entity.DefaultOrder, entity.IsActive };
        dbContext.Entry(entity).Property(nameof(SubStage.IsActive)).CurrentValue = false;
        dbContext.Entry(entity).Property(nameof(SubStage.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Update,
            nameof(SubStage),
            entity.Id.ToString(),
            before: before,
            after: new { entity.Id, entity.MainStageId, entity.Code, entity.Name, entity.Capacity, entity.DefaultOrder, entity.IsActive },
            requestMeta: requestMeta);

        return Result.Success();
    }
}
