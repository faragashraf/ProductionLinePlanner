using System.Data;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

public sealed class ProductModelService(
    AppDbContext dbContext,
    IAuditEngine auditEngine) : IProductModelService
{
    public async Task<PagedResult<ProductModelDto>> GetModelsAsync(
        string? search,
        bool? isActive = true,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 200)
        {
            return PagedResult<ProductModelDto>.Failure(new Error("ValidationError", "page and pageSize must be positive, pageSize max 200."));
        }

        var query = dbContext.ProductModels.AsNoTracking().AsQueryable();
        if (isActive.HasValue)
        {
            query = query.Where(x => x.IsActive == isActive.Value);
        }

        var searchTerm = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim().ToLower()}%";
        if (searchTerm is not null)
        {
            query = query.Where(x =>
                EF.Functions.Like(x.Code.ToLower(), searchTerm) ||
                EF.Functions.Like(x.Name.ToLower(), searchTerm));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ProductModelDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Description = x.Description,
                IsActive = x.IsActive,
                CreatedAtUtc = x.CreatedAtUtc,
                UpdatedAtUtc = x.UpdatedAtUtc
            })
            .ToArrayAsync(cancellationToken);

        return PagedResult<ProductModelDto>.Success(items, page, pageSize, total);
    }

    public async Task<Result<ProductModelDto>> GetModelAsync(Guid modelId, CancellationToken cancellationToken = default)
    {
        if (modelId == Guid.Empty)
        {
            return Result<ProductModelDto>.Failure(new Error("ValidationError", "ModelId is required."));
        }

        var model = await dbContext.ProductModels.AsNoTracking().FirstOrDefaultAsync(x => x.Id == modelId, cancellationToken);
        if (model is null)
        {
            return Result<ProductModelDto>.Failure(new Error("NotFound", "Model not found."));
        }

        return Result<ProductModelDto>.Success(new ProductModelDto
        {
            Id = model.Id,
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            IsActive = model.IsActive,
            CreatedAtUtc = model.CreatedAtUtc,
            UpdatedAtUtc = model.UpdatedAtUtc
        });
    }

    public async Task<Result<ProductModelDto>> CreateModelAsync(
        CreateProductModelRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<ProductModelDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        var code = request.Code?.Trim();
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result<ProductModelDto>.Failure(new Error("ValidationError", "Code is required."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<ProductModelDto>.Failure(new Error("ValidationError", "Name is required."));
        }

        var codeExists = await dbContext.ProductModels.AnyAsync(x => x.Code == code, cancellationToken);
        if (codeExists)
        {
            return Result<ProductModelDto>.Failure(new Error("Conflict", "Model code must be unique."));
        }

        var entity = new ProductModel(
            id: Guid.NewGuid(),
            code: code,
            name: name,
            description: request.Description?.Trim(),
            isActive: request.IsActive);

        dbContext.ProductModels.Add(entity);
        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Create,
            nameof(ProductModel),
            entity.Id.ToString(),
            before: null,
            after: new { entity.Id, entity.Code, entity.Name, entity.Description, entity.IsActive },
            requestMeta: requestMeta);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<ProductModelDto>.Success(new ProductModelDto
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            Description = entity.Description,
            IsActive = entity.IsActive,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc
        });
    }

    public async Task<Result<ProductModelDto>> UpdateModelAsync(
        Guid modelId,
        UpdateProductModelRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<ProductModelDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (modelId == Guid.Empty)
        {
            return Result<ProductModelDto>.Failure(new Error("ValidationError", "ModelId is required."));
        }

        var entity = await dbContext.ProductModels.FirstOrDefaultAsync(x => x.Id == modelId, cancellationToken);
        if (entity is null)
        {
            return Result<ProductModelDto>.Failure(new Error("NotFound", "Model not found."));
        }

        var normalizedCode = request.Code?.Trim();
        var normalizedName = request.Name?.Trim();
        if (request.Code is null && request.Name is null && request.Description is null && request.IsActive is null)
        {
            return Result<ProductModelDto>.Failure(new Error("ValidationError", "No updatable fields were provided."));
        }

        if (normalizedCode is not null && string.IsNullOrWhiteSpace(normalizedCode))
        {
            return Result<ProductModelDto>.Failure(new Error("ValidationError", "Code cannot be empty."));
        }

        if (normalizedName is not null && string.IsNullOrWhiteSpace(normalizedName))
        {
            return Result<ProductModelDto>.Failure(new Error("ValidationError", "Name cannot be empty."));
        }

        if (normalizedCode is not null && !string.Equals(entity.Code, normalizedCode, StringComparison.Ordinal))
        {
            return Result<ProductModelDto>.Failure(new Error("ValidationError", "لا يمكن تعديل الكود بعد إنشاء السجل."));
        }

        if (normalizedName is not null)
        {
            dbContext.Entry(entity).Property(nameof(ProductModel.Name)).CurrentValue = normalizedName;
        }

        if (request.Description is not null)
        {
            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            dbContext.Entry(entity).Property(nameof(ProductModel.Description)).CurrentValue = description;
        }

        if (request.IsActive is not null)
        {
            dbContext.Entry(entity).Property(nameof(ProductModel.IsActive)).CurrentValue = request.IsActive.Value;
        }

        var before = new { entity.Id, entity.Code, entity.Name, entity.Description, entity.IsActive };
        dbContext.Entry(entity).Property(nameof(ProductModel.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Update,
            nameof(ProductModel),
            entity.Id.ToString(),
            before: before,
            after: new { entity.Id, entity.Code, entity.Name, entity.Description, entity.IsActive },
            requestMeta: requestMeta);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<ProductModelDto>.Success(new ProductModelDto
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            Description = entity.Description,
            IsActive = entity.IsActive,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc
        });
    }

    public async Task<Result> SetModelActivationAsync(
        Guid modelId,
        bool isActive,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (modelId == Guid.Empty)
        {
            return Result.Failure(new Error("ValidationError", "ModelId is required."));
        }

        var entity = await dbContext.ProductModels.FirstOrDefaultAsync(x => x.Id == modelId, cancellationToken);
        if (entity is null)
        {
            return Result.Failure(new Error("NotFound", "Model not found."));
        }

        if (entity.IsActive == isActive)
        {
            return Result.Success();
        }

        var before = new { entity.Id, entity.IsActive };
        dbContext.Entry(entity).Property(nameof(ProductModel.IsActive)).CurrentValue = isActive;
        dbContext.Entry(entity).Property(nameof(ProductModel.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Update,
            nameof(ProductModel),
            entity.Id.ToString(),
            before: before,
            after: new { entity.Id, entity.IsActive },
            requestMeta: requestMeta);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result> DeleteModelAsync(
        Guid modelId,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (modelId == Guid.Empty)
        {
            return Result.Failure(new Error("ValidationError", "ModelId is required."));
        }

        var entity = await dbContext.ProductModels.FirstOrDefaultAsync(x => x.Id == modelId, cancellationToken);
        if (entity is null) return Result.Failure(new Error("NotFound", "Model not found."));
        var eligibility = await GetModelDeleteEligibilityAsync(modelId, cancellationToken);
        if (eligibility.IsFailure) return Result.Failure(eligibility.Error!);
        if (!eligibility.Value!.CanDelete) return Result.Failure(new Error("Conflict", eligibility.Value.MessageAr));

        if (!entity.IsActive)
        {
            return Result.Success();
        }

        var before = new { entity.Id, entity.Code, entity.Name, entity.IsActive };
        entity.Deactivate();
        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Delete,
            nameof(ProductModel),
            entity.Id.ToString(),
            before: before,
            after: new { entity.Id, entity.Code, entity.Name, entity.IsActive, Result = "SoftDeleted" },
            requestMeta: requestMeta);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<ProductModelDeleteEligibilityDto>> GetModelDeleteEligibilityAsync(Guid modelId, CancellationToken cancellationToken = default)
    {
        if (modelId == Guid.Empty) return Result<ProductModelDeleteEligibilityDto>.Failure(new Error("ValidationError", "ModelId is required."));
        if (!await dbContext.ProductModels.AsNoTracking().AnyAsync(x => x.Id == modelId, cancellationToken)) return Result<ProductModelDeleteEligibilityDto>.Failure(new Error("NotFound", "Model not found."));
        var stageCount = await dbContext.ProductModelStages.CountAsync(x => x.ProductModelId == modelId, cancellationToken);
        var productionOrderCount = await dbContext.ProductionOrders.CountAsync(x => x.ProductModelId == modelId, cancellationToken);
        var blockers = new List<string>();
        if (stageCount > 0) blockers.Add($"{stageCount} مرحلة موديل");
        if (productionOrderCount > 0) blockers.Add($"{productionOrderCount} تشغيل إنتاج");
        var canDelete = blockers.Count == 0;
        return Result<ProductModelDeleteEligibilityDto>.Success(new ProductModelDeleteEligibilityDto(modelId, canDelete, canDelete ? "يمكن حذف الموديل من الكتالوج التشغيلي." : $"لا يمكن حذف الموديل لأنه مرتبط بـ {string.Join(" و", blockers)}."));
    }

    public async Task<Result<ProductModelStageDto[]>> GetModelStagesAsync(Guid modelId, CancellationToken cancellationToken = default)
    {
        if (modelId == Guid.Empty)
        {
            return Result<ProductModelStageDto[]>.Failure(new Error("ValidationError", "ModelId is required."));
        }

        var exists = await dbContext.ProductModels.AnyAsync(x => x.Id == modelId, cancellationToken);
        if (!exists)
        {
            return Result<ProductModelStageDto[]>.Failure(new Error("NotFound", "Model not found."));
        }

        var items = await dbContext.ProductModelStages
            .AsNoTracking()
            .Where(x => x.ProductModelId == modelId)
            .Include(x => x.SubStage)
            .OrderBy(x => x.StageOrder)
            .Select(x => new ProductModelStageDto
            {
                Id = x.Id,
                ProductModelId = x.ProductModelId,
                SubStageId = x.SubStageId,
                SubStageCode = x.SubStage != null ? x.SubStage.Code : string.Empty,
                SubStageName = x.SubStage != null ? x.SubStage.Name : string.Empty,
                StageOrder = x.StageOrder,
                PiecePrice = x.PiecePrice,
                StandardSeconds = x.StandardSeconds,
                CompensationMode = x.CompensationMode.ToString(),
                IsRequired = x.IsRequired,
                IsActive = x.IsActive,
                EffectiveFrom = x.EffectiveFrom,
                CreatedAtUtc = x.CreatedAtUtc,
                UpdatedAtUtc = x.UpdatedAtUtc
            })
            .ToArrayAsync(cancellationToken);

        return Result<ProductModelStageDto[]>.Success(items);
    }

    public async Task<Result<ProductModelStageDto>> AddModelStageAsync(
        Guid modelId,
        UpsertProductModelStageRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<ProductModelStageDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (modelId == Guid.Empty)
        {
            return Result<ProductModelStageDto>.Failure(new Error("ValidationError", "ModelId is required."));
        }

        if (request.SubStageId is null)
        {
            return Result<ProductModelStageDto>.Failure(new Error("ValidationError", "SubStageId is required."));
        }

        if (request.StageOrder is null)
        {
            return Result<ProductModelStageDto>.Failure(new Error("ValidationError", "StageOrder is required."));
        }

        if (request.PiecePrice is null)
        {
            return Result<ProductModelStageDto>.Failure(new Error("ValidationError", "PiecePrice is required."));
        }

        if (request.CompensationMode is null)
        {
            return Result<ProductModelStageDto>.Failure(new Error("ValidationError", "CompensationMode is required."));
        }

        if (request.PiecePrice < 0m)
        {
            return Result<ProductModelStageDto>.Failure(new Error("ValidationError", "PiecePrice must be greater than or equal to 0."));
        }

        if (request.StandardSeconds is <= 0m)
        {
            return Result<ProductModelStageDto>.Failure(new Error("ValidationError", "StandardSeconds must be greater than 0 when provided."));
        }

        if (request.StageOrder.Value <= 0)
        {
            return Result<ProductModelStageDto>.Failure(new Error("ValidationError", "StageOrder must be greater than zero."));
        }

        var modelExists = await dbContext.ProductModels.AnyAsync(x => x.Id == modelId, cancellationToken);
        if (!modelExists)
        {
            return Result<ProductModelStageDto>.Failure(new Error("NotFound", "Model not found."));
        }

        var subStageExists = await dbContext.SubStages.AnyAsync(
            x => x.Id == request.SubStageId.Value && x.IsActive,
            cancellationToken);
        if (!subStageExists)
        {
            return Result<ProductModelStageDto>.Failure(new Error("NotFound", "Sub stage not found."));
        }

        var duplicateSubStage = await dbContext.ProductModelStages.AnyAsync(
            x => x.ProductModelId == modelId && x.SubStageId == request.SubStageId.Value,
            cancellationToken);
        if (duplicateSubStage)
        {
            return Result<ProductModelStageDto>.Failure(new Error("Conflict", "SubStage was already configured for this model."));
        }

        var duplicateOrder = await dbContext.ProductModelStages.AnyAsync(
            x => x.ProductModelId == modelId && x.StageOrder == request.StageOrder.Value,
            cancellationToken);
        if (duplicateOrder)
        {
            return Result<ProductModelStageDto>.Failure(new Error("Conflict", "StageOrder must be unique per model."));
        }

        var entity = new ProductModelStage(
            id: Guid.NewGuid(),
            productModelId: modelId,
            subStageId: request.SubStageId.Value,
            stageOrder: request.StageOrder.Value,
            piecePrice: request.PiecePrice.Value,
            standardSeconds: request.StandardSeconds,
            compensationMode: request.CompensationMode.Value,
            isRequired: request.IsRequired ?? true,
            isActive: request.IsActive ?? true,
            effectiveFrom: request.EffectiveFrom);

        dbContext.ProductModelStages.Add(entity);
        var subStage = await dbContext.SubStages.AsNoTracking().SingleAsync(x => x.Id == request.SubStageId.Value, cancellationToken);
        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Create,
            nameof(ProductModelStage),
            entity.Id.ToString(),
            before: null,
            after: new { entity.Id, entity.ProductModelId, entity.SubStageId, entity.StageOrder, entity.PiecePrice, entity.StandardSeconds, entity.CompensationMode, entity.IsRequired, entity.IsActive },
            requestMeta: requestMeta);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<ProductModelStageDto>.Success(new ProductModelStageDto
        {
            Id = entity.Id,
            ProductModelId = entity.ProductModelId,
            SubStageId = entity.SubStageId,
            SubStageCode = subStage.Code,
            SubStageName = subStage.Name,
            StageOrder = entity.StageOrder,
            PiecePrice = entity.PiecePrice,
            StandardSeconds = entity.StandardSeconds,
            CompensationMode = entity.CompensationMode.ToString(),
            IsRequired = entity.IsRequired,
            IsActive = entity.IsActive,
            EffectiveFrom = entity.EffectiveFrom,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc
        });
    }

    public async Task<Result<ProductModelStageDto>> UpdateModelStageAsync(
        Guid modelId,
        Guid modelStageId,
        UpsertProductModelStageRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<ProductModelStageDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (modelId == Guid.Empty || modelStageId == Guid.Empty)
        {
            return Result<ProductModelStageDto>.Failure(new Error("ValidationError", "ModelId and ModelStageId are required."));
        }

        if (request.SubStageId is null && request.StageOrder is null && request.PiecePrice is null &&
            !request.HasStandardSeconds && request.StandardSeconds is null && request.CompensationMode is null && request.IsRequired is null && request.IsActive is null &&
            !request.HasEffectiveFrom && request.EffectiveFrom is null)
        {
            return Result<ProductModelStageDto>.Failure(new Error("ValidationError", "No updatable fields were provided."));
        }

        var entity = await dbContext.ProductModelStages
            .FirstOrDefaultAsync(x => x.Id == modelStageId && x.ProductModelId == modelId, cancellationToken);
        if (entity is null)
        {
            return Result<ProductModelStageDto>.Failure(new Error("NotFound", "Product model stage not found."));
        }

        var subStageId = request.SubStageId ?? entity.SubStageId;
        if (request.SubStageId is not null && await dbContext.SubStages.AnyAsync(x => x.Id == request.SubStageId && x.IsActive, cancellationToken) is false)
        {
            return Result<ProductModelStageDto>.Failure(new Error("NotFound", "Sub stage not found."));
        }

        var stageOrder = request.StageOrder ?? entity.StageOrder;
        if (request.StageOrder is not null && request.StageOrder.Value <= 0)
        {
            return Result<ProductModelStageDto>.Failure(new Error("ValidationError", "StageOrder must be greater than zero."));
        }

        if (request.PiecePrice is not null && request.PiecePrice.Value < 0m)
        {
            return Result<ProductModelStageDto>.Failure(new Error("ValidationError", "PiecePrice must be greater than or equal to 0."));
        }

        if ((request.HasStandardSeconds || request.StandardSeconds.HasValue) && request.StandardSeconds is <= 0m)
        {
            return Result<ProductModelStageDto>.Failure(new Error("ValidationError", "StandardSeconds must be greater than 0 when provided."));
        }

        if (request.SubStageId is not null && request.SubStageId.Value != entity.SubStageId)
        {
            var duplicate = await dbContext.ProductModelStages.AnyAsync(
                x => x.Id != entity.Id && x.ProductModelId == modelId && x.SubStageId == request.SubStageId.Value,
                cancellationToken);
            if (duplicate)
            {
                return Result<ProductModelStageDto>.Failure(new Error("Conflict", "SubStage was already configured for this model."));
            }
        }

        if (request.StageOrder is not null)
        {
            var duplicateOrder = await dbContext.ProductModelStages.AnyAsync(
                x => x.Id != entity.Id && x.ProductModelId == modelId && x.StageOrder == stageOrder,
                cancellationToken);
            if (duplicateOrder)
            {
                return Result<ProductModelStageDto>.Failure(new Error("Conflict", "StageOrder must be unique per model."));
            }
        }

        var before = new { entity.Id, entity.SubStageId, entity.StageOrder, entity.PiecePrice, entity.StandardSeconds, entity.CompensationMode, entity.IsRequired, entity.IsActive };
        entity.Update(
            subStageId,
            stageOrder,
            request.PiecePrice ?? entity.PiecePrice,
            request.HasStandardSeconds || request.StandardSeconds.HasValue ? request.StandardSeconds : entity.StandardSeconds,
            request.CompensationMode ?? entity.CompensationMode,
            request.IsRequired ?? entity.IsRequired,
            request.IsActive ?? entity.IsActive,
            request.HasEffectiveFrom || request.EffectiveFrom.HasValue ? request.EffectiveFrom : entity.EffectiveFrom,
            DateTime.UtcNow);

        dbContext.Entry(entity).Property(nameof(ProductModelStage.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
        var subStage = await dbContext.SubStages.AsNoTracking().SingleAsync(x => x.Id == entity.SubStageId, cancellationToken);
        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Update,
            nameof(ProductModelStage),
            entity.Id.ToString(),
            before: before,
            after: new { entity.Id, entity.SubStageId, entity.StageOrder, entity.PiecePrice, entity.StandardSeconds, entity.CompensationMode, entity.IsRequired, entity.IsActive },
            requestMeta: requestMeta);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<ProductModelStageDto>.Success(new ProductModelStageDto
        {
            Id = entity.Id,
            ProductModelId = entity.ProductModelId,
            SubStageId = entity.SubStageId,
            SubStageCode = subStage.Code,
            SubStageName = subStage.Name,
            StageOrder = entity.StageOrder,
            PiecePrice = entity.PiecePrice,
            StandardSeconds = entity.StandardSeconds,
            CompensationMode = entity.CompensationMode.ToString(),
            IsRequired = entity.IsRequired,
            IsActive = entity.IsActive,
            EffectiveFrom = entity.EffectiveFrom,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc
        });
    }

    public async Task<Result> DeactivateModelStageAsync(
        Guid modelId,
        Guid modelStageId,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (modelId == Guid.Empty || modelStageId == Guid.Empty)
        {
            return Result.Failure(new Error("ValidationError", "ModelId and ModelStageId are required."));
        }

        var entity = await dbContext.ProductModelStages
            .FirstOrDefaultAsync(x => x.Id == modelStageId && x.ProductModelId == modelId, cancellationToken);
        if (entity is null)
        {
            return Result.Failure(new Error("NotFound", "Product model stage not found."));
        }

        if (!entity.IsActive)
        {
            return Result.Success();
        }

        var before = new { entity.Id, entity.IsActive };
        entity.Deactivate(DateTime.UtcNow);
        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Update,
            nameof(ProductModelStage),
            entity.Id.ToString(),
            before: before,
            after: new { entity.Id, entity.IsActive },
            requestMeta: requestMeta);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<CopyProductModelStagesSummaryDto>> CopyModelStagesAsync(
        Guid sourceModelId,
        CopyProductModelStagesRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<CopyProductModelStagesSummaryDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (sourceModelId == Guid.Empty || request.SourceProductionLineId == Guid.Empty)
        {
            return Result<CopyProductModelStagesSummaryDto>.Failure(new Error("ValidationError", "Source model and production line are required."));
        }

        if (request.TargetModelId == Guid.Empty || request.TargetProductionLineId == Guid.Empty)
        {
            return Result<CopyProductModelStagesSummaryDto>.Failure(new Error("ValidationError", "Target model and production line are required."));
        }

        if (sourceModelId == request.TargetModelId && request.SourceProductionLineId == request.TargetProductionLineId)
        {
            return Result<CopyProductModelStagesSummaryDto>.Failure(new Error("ValidationError", "Source and target model/line context must be different."));
        }

        if (request.SourceProductModelStageIds is not { Length: > 0 and <= 200 })
        {
            return Result<CopyProductModelStagesSummaryDto>.Failure(new Error("ValidationError", "Select between 1 and 200 model stages to copy."));
        }

        if (request.SourceProductModelStageIds.Any(stageId => stageId == Guid.Empty))
        {
            return Result<CopyProductModelStagesSummaryDto>.Failure(new Error("ValidationError", "Every selected model stage must have a valid identifier."));
        }

        await using var transaction = request.PreviewOnly
            ? null
            : await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var modelIds = new[] { sourceModelId, request.TargetModelId }.Distinct().ToArray();
        var existingModelIds = await dbContext.ProductModels
            .AsNoTracking()
            .Where(model => modelIds.Contains(model.Id))
            .Select(model => model.Id)
            .ToArrayAsync(cancellationToken);
        if (existingModelIds.Length != modelIds.Length)
        {
            return Result<CopyProductModelStagesSummaryDto>.Failure(new Error("NotFound", "Source or target model was not found."));
        }

        var lineIds = new[] { request.SourceProductionLineId, request.TargetProductionLineId }.Distinct().ToArray();
        var existingLines = await dbContext.ProductionLines
            .AsNoTracking()
            .Where(line => lineIds.Contains(line.Id))
            .Select(line => new { line.Id, line.IsActive })
            .ToArrayAsync(cancellationToken);
        if (existingLines.Length != lineIds.Length)
        {
            return Result<CopyProductModelStagesSummaryDto>.Failure(new Error("NotFound", "Source or target production line was not found."));
        }
        if (existingLines.Single(line => line.Id == request.TargetProductionLineId).IsActive == false)
        {
            return Result<CopyProductModelStagesSummaryDto>.Failure(new Error("ValidationError", "Target production line must be active."));
        }

        var requestedSourceIds = request.SourceProductModelStageIds.Distinct().ToArray();
        var sourceStages = await dbContext.ProductModelStages
            .AsNoTracking()
            .Include(stage => stage.SubStage)
            .Where(stage => requestedSourceIds.Contains(stage.Id))
            .ToDictionaryAsync(stage => stage.Id, cancellationToken);
        var targetStages = await dbContext.SubStages
            .AsNoTracking()
            .Where(stage => stage.ProductionLineId == request.TargetProductionLineId)
            .ToArrayAsync(cancellationToken);
        var targetRelations = await dbContext.ProductModelStages
            .AsNoTracking()
            .Where(stage => stage.ProductModelId == request.TargetModelId
                && stage.SubStage != null
                && stage.SubStage.ProductionLineId == request.TargetProductionLineId)
            .Select(stage => new { stage.SubStageId, stage.StageOrder })
            .ToArrayAsync(cancellationToken);
        var targetModelStageOrders = await dbContext.ProductModelStages
            .AsNoTracking()
            .Where(stage => stage.ProductModelId == request.TargetModelId)
            .Select(stage => stage.StageOrder)
            .ToArrayAsync(cancellationToken);
        var targetGroups = await dbContext.MainStages
            .AsNoTracking()
            .Where(stage => stage.ProductionLineId == request.TargetProductionLineId)
            .OrderBy(stage => stage.SequenceOrder)
            .ThenBy(stage => stage.Name)
            .ThenBy(stage => stage.Id)
            .ToArrayAsync(cancellationToken);

        var validationErrors = new List<string>();
        if (requestedSourceIds.Length != request.SourceProductModelStageIds.Length)
        {
            validationErrors.Add("لا يمكن تكرار علاقة مرحلة مصدر داخل الطلب نفسه.");
        }

        var sourceCandidates = new List<ProductModelStage>();
        foreach (var sourceProductModelStageId in request.SourceProductModelStageIds)
        {
            if (!sourceStages.TryGetValue(sourceProductModelStageId, out var sourceStage)
                || sourceStage.ProductModelId != sourceModelId
                || sourceStage.SubStage?.ProductionLineId != request.SourceProductionLineId)
            {
                validationErrors.Add($"علاقة مرحلة المصدر {sourceProductModelStageId} لا تنتمي إلى سياق المصدر المختار.");
                continue;
            }

            sourceCandidates.Add(sourceStage);
        }

        if (validationErrors.Count > 0)
        {
            return Result<CopyProductModelStagesSummaryDto>.Success(new CopyProductModelStagesSummaryDto
            {
                IsPreview = request.PreviewOnly,
                RequestedCount = request.SourceProductModelStageIds.Length,
                FailedCount = validationErrors.Count,
                ValidationErrors = validationErrors.Distinct().ToArray()
            });
        }

        var targetStagesByCode = targetStages.ToDictionary(stage => stage.Code, StringComparer.OrdinalIgnoreCase);
        var targetRelationStageIds = targetRelations.Select(stage => stage.SubStageId).ToHashSet();
        var candidates = new List<CopyCandidate>();
        var skipped = new List<CopyProductModelStageSkipDto>();
        var failed = new List<CopyProductModelStageFailureDto>();
        foreach (var sourceStage in sourceCandidates.OrderBy(stage => stage.StageOrder).ThenBy(stage => stage.Id))
        {
            var sourceCatalogStage = sourceStage.SubStage!;
            if (!targetStagesByCode.TryGetValue(sourceCatalogStage.Code, out var existingTargetStage))
            {
                candidates.Add(new CopyCandidate(sourceStage, null));
                continue;
            }

            if (!RepresentsSameCatalogStage(sourceCatalogStage, existingTargetStage))
            {
                failed.Add(new CopyProductModelStageFailureDto
                {
                    SourceProductModelStageId = sourceStage.Id,
                    SubStageId = sourceCatalogStage.Id,
                    StageCode = sourceCatalogStage.Code,
                    StageName = sourceCatalogStage.Name,
                    ReasonCode = "TargetStageCodeConflict",
                    Reason = $"يوجد على خط الإنتاج الهدف مرحلة أخرى بالكود {sourceCatalogStage.Code}. لم يتم استبدالها أو تعديلها."
                });
                continue;
            }

            if (targetRelationStageIds.Contains(existingTargetStage.Id))
            {
                skipped.Add(CreateSkippedStage(
                    sourceStage,
                    "AlreadyLinked",
                    "المرحلة موجودة بالفعل ضمن الموديل الهدف وخط الإنتاج الهدف."));
                continue;
            }

            candidates.Add(new CopyCandidate(sourceStage, existingTargetStage));
        }

        static CopyProductModelStageSkipDto CreateSkippedStage(ProductModelStage sourceStage, string reasonCode, string reason) =>
            new()
            {
                SourceProductModelStageId = sourceStage.Id,
                SubStageId = sourceStage.SubStageId,
                StageCode = sourceStage.SubStage?.Code ?? string.Empty,
                StageName = sourceStage.SubStage?.Name ?? string.Empty,
                ReasonCode = reasonCode,
                Reason = reason
            };

        var activeTargetGroup = targetGroups.FirstOrDefault(group => group.IsActive);
        var targetGroupId = activeTargetGroup?.Id ?? Guid.NewGuid();
        var occupiedCatalogOrders = targetStages
            .Where(stage => stage.MainStageId == targetGroupId)
            .Select(stage => stage.DefaultOrder)
            .ToHashSet();
        var nextCatalogOrder = occupiedCatalogOrders.Count == 0 ? 1 : occupiedCatalogOrders.Max() + 1;
        var occupiedModelOrders = targetModelStageOrders.ToHashSet();
        var preserveSourceOrders = candidates.All(candidate => !occupiedModelOrders.Contains(candidate.SourceStage.StageOrder));
        var nextModelOrder = targetModelStageOrders.Length == 0 ? 1 : targetModelStageOrders.Max() + 1;
        var plans = candidates.Select((candidate, index) =>
        {
            var targetStageOrder = candidate.ExistingTargetStage?.DefaultOrder
                ?? AllocateTargetStageOrder(candidate.SourceStage.SubStage!.DefaultOrder, occupiedCatalogOrders, ref nextCatalogOrder);
            return new CopyPlan(
                candidate,
                preserveSourceOrders ? candidate.SourceStage.StageOrder : nextModelOrder + index,
                targetStageOrder,
                candidate.ExistingTargetStage?.Id ?? Guid.NewGuid(),
                targetGroupId);
        }).ToArray();

        if (request.PreviewOnly)
        {
            return Result<CopyProductModelStagesSummaryDto>.Success(BuildCopySummary(
                true,
                request.SourceProductModelStageIds.Length,
                plans,
                skipped,
                failed,
                []));
        }

        if (failed.Count > 0)
        {
            return Result<CopyProductModelStagesSummaryDto>.Success(BuildCopySummary(
                false,
                request.SourceProductModelStageIds.Length,
                [],
                skipped,
                failed,
                []));
        }

        var createdAtUtc = DateTime.UtcNow;
        MainStage? createdTargetGroup = null;
        if (plans.Any(plan => plan.Candidate.ExistingTargetStage is null) && activeTargetGroup is null)
        {
            var nextGroupOrder = targetGroups.Length == 0 ? 1 : targetGroups.Max(group => group.SequenceOrder) + 1;
            createdTargetGroup = new MainStage(
                targetGroupId,
                request.TargetProductionLineId,
                "Internal operational stage group",
                nextGroupOrder,
                createdAtUtc: createdAtUtc);
            dbContext.MainStages.Add(createdTargetGroup);
        }

        var addedSubStages = plans
            .Where(plan => plan.Candidate.ExistingTargetStage is null)
            .Select(plan => new SubStage(
                plan.ResultSubStageId,
                plan.TargetMainStageId,
                plan.Candidate.SourceStage.SubStage!.Name,
                plan.Candidate.SourceStage.SubStage.Code,
                plan.Candidate.SourceStage.SubStage.Capacity,
                plan.TargetStageOrder,
                plan.Candidate.SourceStage.SubStage.IsActive,
                createdAtUtc,
                request.TargetProductionLineId))
            .ToArray();
        dbContext.SubStages.AddRange(addedSubStages);

        var addedEntities = plans.Select(plan => new ProductModelStage(
            id: Guid.NewGuid(),
            productModelId: request.TargetModelId,
            subStageId: plan.ResultSubStageId,
            stageOrder: plan.StageOrder,
            piecePrice: plan.Candidate.SourceStage.PiecePrice,
            standardSeconds: plan.Candidate.SourceStage.StandardSeconds,
            compensationMode: plan.Candidate.SourceStage.CompensationMode,
            isRequired: plan.Candidate.SourceStage.IsRequired,
            isActive: plan.Candidate.SourceStage.IsActive,
            effectiveFrom: plan.Candidate.SourceStage.EffectiveFrom,
            createdAtUtc: createdAtUtc)).ToArray();
        dbContext.ProductModelStages.AddRange(addedEntities);

        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Create,
            nameof(ProductModelStage),
            request.TargetModelId.ToString(),
            before: null,
            after: new
            {
                SourceModelId = sourceModelId,
                TargetModelId = request.TargetModelId,
                request.SourceProductionLineId,
                request.TargetProductionLineId,
                RequestedCount = request.SourceProductModelStageIds.Length,
                AddedCount = addedEntities.Length,
                SkippedCount = skipped.Count,
                FailedCount = 0,
                AddedStageIds = addedEntities.Select(stage => stage.Id).ToArray(),
                AddedSubStageIds = addedSubStages.Select(stage => stage.Id).ToArray(),
                SkippedStageIds = skipped.Select(stage => stage.SubStageId).ToArray(),
                request.Note
            },
            requestMeta: requestMeta,
            cancellationToken: cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction!.CommitAsync(cancellationToken);

        return Result<CopyProductModelStagesSummaryDto>.Success(
            BuildCopySummary(
                false,
                request.SourceProductModelStageIds.Length,
                plans,
                skipped,
                failed,
                addedEntities.Select(stage => stage.Id).ToArray()));
    }

    private static bool RepresentsSameCatalogStage(SubStage source, SubStage target) =>
        string.Equals(source.Code.Trim(), target.Code.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(source.Name.Trim(), target.Name.Trim(), StringComparison.OrdinalIgnoreCase)
        && source.Capacity == target.Capacity
        && source.IsActive == target.IsActive;

    private static int AllocateTargetStageOrder(int preferredOrder, HashSet<int> occupiedOrders, ref int nextOrder)
    {
        if (preferredOrder > 0 && occupiedOrders.Add(preferredOrder))
        {
            if (preferredOrder >= nextOrder) nextOrder = preferredOrder + 1;
            return preferredOrder;
        }

        while (!occupiedOrders.Add(nextOrder)) nextOrder++;
        return nextOrder++;
    }

    private static CopyProductModelStagesSummaryDto BuildCopySummary(
        bool isPreview,
        int requestedCount,
        IReadOnlyList<CopyPlan> plans,
        IReadOnlyList<CopyProductModelStageSkipDto> skipped,
        IReadOnlyList<CopyProductModelStageFailureDto> failed,
        Guid[] addedStageIds) =>
        new()
        {
            IsPreview = isPreview,
            RequestedCount = requestedCount,
            AddedCount = plans.Count,
            SkippedCount = skipped.Count,
            FailedCount = failed.Count,
            AddedStageIds = addedStageIds,
            PlannedStages = plans.Select(plan => new CopyProductModelStagePlanDto
            {
                SourceProductModelStageId = plan.Candidate.SourceStage.Id,
                SubStageId = plan.Candidate.SourceStage.SubStageId,
                SubStageCode = plan.Candidate.SourceStage.SubStage?.Code ?? string.Empty,
                SubStageName = plan.Candidate.SourceStage.SubStage?.Name ?? string.Empty,
                StageOrder = plan.StageOrder,
                TargetStageOrder = plan.TargetStageOrder,
                CreatesTargetStage = plan.Candidate.ExistingTargetStage is null,
                StatusLabel = plan.Candidate.ExistingTargetStage is null
                    ? "ستُنشأ على الخط الهدف ثم ترتبط بالموديل."
                    : "المرحلة موجودة على الخط الهدف وسترتبط بالموديل."
            }).ToArray(),
            SkippedStages = skipped.ToArray(),
            FailedStages = failed.ToArray()
        };

    private sealed record CopyCandidate(ProductModelStage SourceStage, SubStage? ExistingTargetStage);
    private sealed record CopyPlan(
        CopyCandidate Candidate,
        int StageOrder,
        int TargetStageOrder,
        Guid ResultSubStageId,
        Guid TargetMainStageId);

}
