using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.SqlClient;
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
    IAuditEngine auditEngine,
    IStageDependencyInspector dependencyInspector) : IProductionStageCatalogService
{
    public async Task<PagedResult<MainStageDto>> GetMainStagesAsync(
        Guid? departmentId,
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
        if (departmentId.HasValue)
        {
            query = query.Where(x => x.DepartmentId == departmentId.Value);
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
                DepartmentId = x.DepartmentId,
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
            DepartmentId = entity.DepartmentId,
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
                MainStageName = x.MainStage!.Name,
                DepartmentId = x.DepartmentId,
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

        var entity = await dbContext.SubStages.AsNoTracking().Include(x => x.MainStage).FirstOrDefaultAsync(x => x.Id == subStageId, cancellationToken);
        if (entity is null)
        {
            return Result<SubStageDto>.Failure(new Error("NotFound", "Sub stage not found."));
        }

        return Result<SubStageDto>.Success(new SubStageDto
        {
            Id = entity.Id,
            MainStageId = entity.MainStageId,
            MainStageName = entity.MainStage?.Name,
            DepartmentId = entity.DepartmentId,
            Code = entity.Code,
            Name = entity.Name,
            Capacity = entity.Capacity,
            DefaultOrder = entity.DefaultOrder,
            IsActive = entity.IsActive
        });
    }

    public async Task<Result<MainStageDto>> CreateMainStageAsync(
        Guid departmentId,
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

        if (departmentId == Guid.Empty)
        {
            return Result<MainStageDto>.Failure(new Error("ValidationError", "DepartmentId is required."));
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

        var departmentExists = await dbContext.Departments.AnyAsync(x => x.Id == departmentId && x.IsActive, cancellationToken);
        if (!departmentExists)
        {
            return Result<MainStageDto>.Failure(new Error("NotFound", "القسم غير موجود أو غير نشط."));
        }

        var conflict = await dbContext.MainStages.AnyAsync(
            x => x.DepartmentId == departmentId && x.Name == normalizedName,
            cancellationToken);
        if (conflict)
        {
            return Result<MainStageDto>.Failure(new Error("Conflict", "يوجد مستوى رئيسي بالاسم نفسه داخل القسم."));
        }

        var entity = new MainStage(
            id: Guid.NewGuid(),
            departmentId: departmentId,
            name: normalizedName,
            isCritical: isCritical,
            sequenceOrder: sequenceOrder,
            isActive: isActive);

        dbContext.MainStages.Add(entity);
        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Create,
            nameof(MainStage),
            entity.Id.ToString(),
            before: null,
            after: new { entity.Id, entity.DepartmentId, entity.Name, entity.IsCritical, entity.SequenceOrder, entity.IsActive },
            requestMeta: requestMeta);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<MainStageDto>.Success(new MainStageDto
        {
            Id = entity.Id,
            DepartmentId = entity.DepartmentId,
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

        if (sequenceOrder is not null && sequenceOrder.Value < 0)
        {
            return Result<MainStageDto>.Failure(new Error("ValidationError", "SequenceOrder must be zero or greater."));
        }

        if (normalizedName is not null && !string.Equals(normalizedName, entity.Name, StringComparison.Ordinal))
        {
            var duplicateName = await dbContext.MainStages.AnyAsync(
                x => x.Id != entity.Id && x.DepartmentId == entity.DepartmentId && x.Name == normalizedName,
                cancellationToken);
            if (duplicateName)
            {
                return Result<MainStageDto>.Failure(new Error("Conflict", "يوجد مستوى رئيسي بالاسم نفسه داخل القسم."));
            }
        }

        var before = new { entity.Id, entity.DepartmentId, entity.Name, entity.IsCritical, entity.SequenceOrder, entity.IsActive };
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
        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Update,
            nameof(MainStage),
            entity.Id.ToString(),
            before,
            new { entity.Id, entity.DepartmentId, entity.Name, entity.IsCritical, entity.SequenceOrder, entity.IsActive },
            requestMeta);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<MainStageDto>.Success(new MainStageDto
        {
            Id = entity.Id,
            DepartmentId = entity.DepartmentId,
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

        var before = new { entity.Id, entity.DepartmentId, entity.Name, entity.IsCritical, entity.SequenceOrder, entity.IsActive };
        dbContext.Entry(entity).Property(nameof(MainStage.IsActive)).CurrentValue = false;
        dbContext.Entry(entity).Property(nameof(MainStage.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Update,
            nameof(MainStage),
            entity.Id.ToString(),
            before: before,
            after: new { entity.Id, entity.DepartmentId, entity.Name, entity.IsCritical, entity.SequenceOrder, entity.IsActive },
            requestMeta: requestMeta);
        await dbContext.SaveChangesAsync(cancellationToken);

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
        _ = code; // Legacy aliases retain their payload shape; codes are generated server-side.
        _ = defaultOrder; // Operational ordering is allocated by the catalog under a transaction.
        var mainStage = await dbContext.MainStages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == mainStageId, cancellationToken);
        if (mainStage is null) return Result<SubStageDto>.Failure(new Error("NotFound", "Main stage was not found."));
        return await CreateOperationalStageCoreAsync(mainStage.DepartmentId, mainStageId, name, capacity, isActive, actorUserId, requestMeta, cancellationToken);
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

        if (normalizedCode is not null && !string.Equals(normalizedCode, entity.Code, StringComparison.Ordinal))
        {
            return Result<SubStageDto>.Failure(new Error("ValidationError", "لا يمكن تعديل الكود بعد إنشاء السجل."));
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
            entity.DepartmentId,
            entity.Code,
            entity.Name,
            entity.Capacity,
            entity.DefaultOrder,
            entity.IsActive
        };

        if (normalizedCode is not null || normalizedName is not null || defaultOrder is not null || capacity is not null)
        {
            entity.Rename(normalizedName ?? entity.Name);
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
            if (!isActive.Value && entity.IsActive)
            {
                var dependencies = await dependencyInspector.InspectAsync(subStageId, cancellationToken);
                if (dependencies.IsFailure) return Result<SubStageDto>.Failure(dependencies.Error!);
                if (!dependencies.Value!.CanDisable) return Result<SubStageDto>.Failure(new Error("Conflict", dependencies.Value.DisableMessageAr));
            }
            dbContext.Entry(entity).Property(nameof(SubStage.IsActive)).CurrentValue = isActive.Value;
        }

        dbContext.Entry(entity).Property(nameof(SubStage.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Update,
            nameof(SubStage),
            entity.Id.ToString(),
            before: before,
            after: new { entity.Id, entity.MainStageId, entity.DepartmentId, entity.Code, entity.Name, entity.Capacity, entity.DefaultOrder, entity.IsActive },
            requestMeta: requestMeta);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<SubStageDto>.Success(new SubStageDto
        {
            Id = entity.Id,
            MainStageId = entity.MainStageId,
            DepartmentId = entity.DepartmentId,
            Code = entity.Code,
            Name = entity.Name,
            Capacity = entity.Capacity,
            DefaultOrder = entity.DefaultOrder,
            IsActive = entity.IsActive
        });
    }

    public async Task<Result<SubStageDto>> DeactivateSubStageAsync(
        Guid subStageId,
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
            return Result<SubStageDto>.Failure(new Error("ValidationError", "StageId is required."));
        }

        var entity = await dbContext.SubStages.FirstOrDefaultAsync(x => x.Id == subStageId, cancellationToken);
        if (entity is null)
        {
            return Result<SubStageDto>.Failure(new Error("NotFound", "المرحلة غير موجودة."));
        }

        // A retry or a duplicate browser submission must not turn a completed
        // deactivation into a false 404. The persisted entity is the response.
        if (!entity.IsActive) return Result<SubStageDto>.Success(ToDto(entity));

        var dependencies = await dependencyInspector.InspectAsync(subStageId, cancellationToken);
        if (dependencies.IsFailure) return Result<SubStageDto>.Failure(dependencies.Error!);
        if (!dependencies.Value!.CanDisable) return Result<SubStageDto>.Failure(new Error("Conflict", dependencies.Value.DisableMessageAr));

        var before = new { entity.Id, entity.MainStageId, entity.DepartmentId, entity.Code, entity.Name, entity.Capacity, entity.DefaultOrder, entity.IsActive };
        dbContext.Entry(entity).Property(nameof(SubStage.IsActive)).CurrentValue = false;
        dbContext.Entry(entity).Property(nameof(SubStage.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Update,
            nameof(SubStage),
            entity.Id.ToString(),
            before: before,
            after: new { entity.Id, entity.MainStageId, entity.DepartmentId, entity.Code, entity.Name, entity.Capacity, entity.DefaultOrder, entity.IsActive },
            requestMeta: requestMeta);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<SubStageDto>.Success(ToDto(entity));
    }

    public async Task<PagedResult<SubStageDto>> GetOperationalStagesAsync(
        Guid? factoryId,
        Guid? departmentId,
        string? name,
        string? code,
        bool? isActive,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 200)
        {
            return PagedResult<SubStageDto>.Failure(new Error("ValidationError", "page and pageSize must be positive, pageSize max 200."));
        }

        var query = dbContext.SubStages.AsNoTracking().AsQueryable();
        if (factoryId.HasValue) query = query.Where(x => x.MainStage!.Department!.FactoryId == factoryId.Value);
        if (departmentId.HasValue) query = query.Where(x => x.DepartmentId == departmentId.Value);
        if (isActive.HasValue) query = query.Where(x => x.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(name))
        {
            var pattern = $"%{name.Trim()}%";
            query = query.Where(x => EF.Functions.Like(x.Name, pattern));
        }
        if (!string.IsNullOrWhiteSpace(code))
        {
            var pattern = $"%{code.Trim()}%";
            query = query.Where(x => EF.Functions.Like(x.Code, pattern));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.DepartmentId).ThenBy(x => x.MainStageId).ThenBy(x => x.DefaultOrder).ThenBy(x => x.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new SubStageDto
            {
                Id = x.Id, MainStageId = x.MainStageId, DepartmentId = x.DepartmentId,
                MainStageName = x.MainStage!.Name,
                FactoryId = x.MainStage!.Department!.FactoryId,
                FactoryName = x.MainStage.Department.Factory!.Name, DepartmentNameAr = x.MainStage.Department.NameAr,
                Code = x.Code, Name = x.Name, Capacity = x.Capacity, DefaultOrder = x.DefaultOrder, IsActive = x.IsActive
            }).ToArrayAsync(cancellationToken);
        return PagedResult<SubStageDto>.Success(items, page, pageSize, total);
    }

    public async Task<Result<SubStageDto>> CreateOperationalStageAsync(
        Guid departmentId,
        string name,
        int capacity,
        bool isActive,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
        => await CreateOperationalStageCoreAsync(departmentId, null, name, capacity, isActive, actorUserId, requestMeta, cancellationToken);

    private async Task<Result<SubStageDto>> CreateOperationalStageCoreAsync(
        Guid departmentId,
        Guid? requestedMainStageId,
        string name,
        int capacity,
        bool isActive,
        Guid actorUserId,
        string? requestMeta,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty) return Result<SubStageDto>.Failure(new Error("Unauthorized", "User context is required."));
        if (departmentId == Guid.Empty) return Result<SubStageDto>.Failure(new Error("ValidationError", "DepartmentId is required."));
        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName)) return Result<SubStageDto>.Failure(new Error("ValidationError", "Name is required."));
        if (capacity < 0) return Result<SubStageDto>.Failure(new Error("ValidationError", "Capacity must be zero or greater."));

        if (!await dbContext.Departments.AnyAsync(x => x.Id == departmentId && x.IsActive, cancellationToken))
        {
            return Result<SubStageDto>.Failure(new Error("NotFound", "القسم غير موجود أو غير نشط."));
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
                : null;
            try
            {
                var groupResult = await ResolveStageGroupAsync(departmentId, requestedMainStageId, actorUserId, requestMeta, cancellationToken);
                if (groupResult.IsFailure)
                {
                    if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                    return Result<SubStageDto>.Failure(groupResult.Error!);
                }
                var selectedGroup = groupResult.Value!;
                // Serializable plus IX_SubStages_MainStageId_SequenceOrder protects this range on SQL Server,
                // so Max + 1 is allocated only while the compatibility group is locked.
                var highestDefaultOrder = await dbContext.SubStages
                    .Where(stage => stage.MainStageId == selectedGroup.Id)
                    .Select(stage => (int?)stage.DefaultOrder)
                    .MaxAsync(cancellationToken) ?? 0;
                if (highestDefaultOrder == int.MaxValue)
                {
                    if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                    return Result<SubStageDto>.Failure(new Error("Conflict", "تعذر تحديد ترتيب المرحلة تلقائيًا. أعد المحاولة."));
                }
                var defaultOrder = highestDefaultOrder + 1;
                var code = await AllocateStageCodeAsync(cancellationToken);
                if (await dbContext.SubStages.AnyAsync(x => x.Code == code, cancellationToken))
                {
                    if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                    continue;
                }

                var entity = new SubStage(Guid.NewGuid(), selectedGroup.Id, normalizedName, code, capacity, defaultOrder, isActive, departmentId: departmentId);
                dbContext.SubStages.Add(entity);
                await auditEngine.RecordAsync(actorUserId, AuditActionType.Create, nameof(SubStage), entity.Id.ToString(), after: new { entity.Id, entity.MainStageId, entity.DepartmentId, entity.Code, entity.Name, entity.Capacity, entity.DefaultOrder, entity.IsActive }, requestMeta: requestMeta, cancellationToken: cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return Result<SubStageDto>.Success(ToDto(entity, selectedGroup.Name));
            }
            catch (Exception exception) when (attempt < 2 && IsConfirmedAllocationConcurrencyConflict(exception))
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
            }
        }

        return Result<SubStageDto>.Failure(new Error("Conflict", "تعذر تحديد ترتيب المرحلة تلقائيًا بسبب إنشاء متزامن. أعد المحاولة."));
    }

    private static bool IsConfirmedAllocationConcurrencyConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 1205 or 2601 or 2627 }) return true;

            // SQLite is used only by automated concurrency tests. Error 5 is a lock conflict and
            // error 19 is the equivalent unique-index conflict that the retry resolves safely.
            if (string.Equals(current.GetType().FullName, "Microsoft.Data.Sqlite.SqliteException", StringComparison.Ordinal)
                && current.GetType().GetProperty("SqliteErrorCode")?.GetValue(current) is int sqliteError
                && sqliteError is 5 or 19) return true;
        }

        return false;
    }

    public async Task<Result<StageDependencySummaryDto>> GetSubStageDependenciesAsync(Guid subStageId, CancellationToken cancellationToken = default) =>
        await dependencyInspector.InspectAsync(subStageId, cancellationToken);

    public async Task<Result> DeleteSubStageAsync(Guid subStageId, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty) return Result.Failure(new Error("Unauthorized", "User context is required."));
        var entity = await dbContext.SubStages.FirstOrDefaultAsync(x => x.Id == subStageId, cancellationToken);
        if (entity is null) return Result.Failure(new Error("NotFound", "Operational stage not found."));
        var dependencies = await dependencyInspector.InspectAsync(subStageId, cancellationToken);
        if (dependencies.IsFailure) return Result.Failure(dependencies.Error!);
        if (!dependencies.Value!.CanDelete) return Result.Failure(new Error("Conflict", dependencies.Value.DeleteMessageAr));
        await auditEngine.RecordAsync(actorUserId, AuditActionType.Delete, nameof(SubStage), entity.Id.ToString(), new { entity.Id, entity.MainStageId, entity.DepartmentId, entity.Code, entity.Name }, null, requestMeta, cancellationToken);
        dbContext.SubStages.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<string> AllocateStageCodeAsync(CancellationToken cancellationToken)
    {
        long value;
        if (dbContext.Database.IsSqlServer())
        {
            // SqlQueryRaw composes scalar SQL as a derived table. SQL Server 2016 forbids
            // NEXT VALUE FOR in that context, so execute the sequence statement directly
            // on the connection enlisted in the current allocation transaction.
            var connection = dbContext.Database.GetDbConnection();
            var openedHere = connection.State != System.Data.ConnectionState.Open;
            if (openedHere) await connection.OpenAsync(cancellationToken);
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT NEXT VALUE FOR [StageCodeSequence]";
                if (dbContext.Database.CurrentTransaction is { } transaction)
                {
                    command.Transaction = transaction.GetDbTransaction();
                }

                value = await command.ExecuteScalarAsync(cancellationToken) is long nextValue
                    ? nextValue
                    : throw new InvalidOperationException("StageCodeSequence did not return a bigint value.");
            }
            finally
            {
                if (openedHere) await connection.CloseAsync();
            }
        }
        else
        {
            // Non-SQL providers are used only by automated tests; production SQL Server always uses the sequence above.
            var codes = await dbContext.SubStages.AsNoTracking().Select(x => x.Code).ToArrayAsync(cancellationToken);
            value = codes.Select(ParseLegacyStageNumber).DefaultIfEmpty(0L).Max() + 1;
        }
        return $"STG{value:000}";
    }

    private static long ParseLegacyStageNumber(string code) =>
        code.StartsWith("STG", StringComparison.OrdinalIgnoreCase) && long.TryParse(code[3..], out var value) ? value : 0;

    private static SubStageDto ToDto(SubStage entity, string? mainStageName = null) => new()
    {
        Id = entity.Id, MainStageId = entity.MainStageId, DepartmentId = entity.DepartmentId,
        MainStageName = mainStageName ?? entity.MainStage?.Name,
        Code = entity.Code, Name = entity.Name, Capacity = entity.Capacity, DefaultOrder = entity.DefaultOrder, IsActive = entity.IsActive
    };

    /// <summary>
    /// MainStage remains a mandatory legacy parent. New operational-stage flows
    /// never expose it: an explicit legacy request is honored for compatibility,
    /// otherwise the first active group is selected deterministically. A single
    /// internal group is created only when a department has no active group at all.
    /// </summary>
    private async Task<Result<MainStage>> ResolveStageGroupAsync(
        Guid departmentId,
        Guid? requestedMainStageId,
        Guid actorUserId,
        string? requestMeta,
        CancellationToken cancellationToken)
    {
        var activeGroups = await dbContext.MainStages
            .Where(stage => stage.DepartmentId == departmentId && stage.IsActive)
            .OrderBy(stage => stage.SequenceOrder).ThenBy(stage => stage.Name).ThenBy(stage => stage.Id)
            .ToArrayAsync(cancellationToken);

        if (requestedMainStageId.HasValue)
        {
            var requested = activeGroups.FirstOrDefault(stage => stage.Id == requestedMainStageId.Value);
            return requested is null
                ? Result<MainStage>.Failure(new Error("ValidationError", "يجب أن يتبع المستوى الرئيسي القسم المحدد وأن يكون نشطًا."))
                : Result<MainStage>.Success(requested);
        }

        if (activeGroups.Length > 0) return Result<MainStage>.Success(activeGroups[0]);

        var nextSequenceOrder = await dbContext.MainStages
            .Where(stage => stage.DepartmentId == departmentId)
            .Select(stage => (int?)stage.SequenceOrder)
            .MaxAsync(cancellationToken) ?? 0;
        var group = new MainStage(Guid.NewGuid(), departmentId, "مجموعة المراحل التشغيلية", nextSequenceOrder + 1);
        dbContext.MainStages.Add(group);
        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Create,
            nameof(MainStage),
            group.Id.ToString(),
            after: new { group.Id, group.DepartmentId, group.SequenceOrder, Purpose = "OperationalStageCompatibility" },
            requestMeta: requestMeta,
            cancellationToken: cancellationToken);
        return Result<MainStage>.Success(group);
    }
}
