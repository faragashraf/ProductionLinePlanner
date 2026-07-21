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

    public async Task<Result> CopyModelStagesAsync(
        Guid sourceModelId,
        CopyProductModelStagesRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (sourceModelId == Guid.Empty)
        {
            return Result.Failure(new Error("ValidationError", "SourceModelId is required."));
        }

        if (request.TargetModelId == Guid.Empty)
        {
            return Result.Failure(new Error("ValidationError", "TargetModelId is required."));
        }

        if (sourceModelId == request.TargetModelId)
        {
            return Result.Failure(new Error("ValidationError", "Source and target models must be different."));
        }

        var sourceExists = await dbContext.ProductModels.AnyAsync(x => x.Id == sourceModelId, cancellationToken);
        if (!sourceExists)
        {
            return Result.Failure(new Error("NotFound", "Source model not found."));
        }

        var targetExists = await dbContext.ProductModels.AnyAsync(x => x.Id == request.TargetModelId, cancellationToken);
        if (!targetExists)
        {
            return Result.Failure(new Error("NotFound", "Target model not found."));
        }

        var sourceStages = await dbContext.ProductModelStages
            .AsNoTracking()
            .Where(x => x.ProductModelId == sourceModelId)
            .ToListAsync(cancellationToken);

        if (sourceStages.Count == 0)
        {
            return Result.Success();
        }

        var targetExisting = await dbContext.ProductModelStages
            .AsNoTracking()
            .Where(x => x.ProductModelId == request.TargetModelId)
            .Select(x => new { x.SubStageId, x.StageOrder })
            .ToListAsync(cancellationToken);

        if (sourceStages.Any(stage => targetExisting.Any(existing => existing.SubStageId == stage.SubStageId)))
        {
            return Result.Failure(new Error("Conflict", "Target model already has stage entries for one or more source stages."));
        }

        if (sourceStages.Any(stage => targetExisting.Any(existing => existing.StageOrder == stage.StageOrder)))
        {
            return Result.Failure(new Error("Conflict", "Target model already has stage entries for one or more source stage orders."));
        }

        foreach (var sourceStage in sourceStages)
        {
            var clone = new ProductModelStage(
                id: Guid.NewGuid(),
                productModelId: request.TargetModelId,
                subStageId: sourceStage.SubStageId,
                stageOrder: sourceStage.StageOrder,
                piecePrice: sourceStage.PiecePrice,
                standardSeconds: sourceStage.StandardSeconds,
                compensationMode: sourceStage.CompensationMode,
                isRequired: sourceStage.IsRequired,
                isActive: sourceStage.IsActive,
                effectiveFrom: sourceStage.EffectiveFrom,
                createdAtUtc: DateTime.UtcNow);

            dbContext.ProductModelStages.Add(clone);
        }

        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Create,
            nameof(ProductModelStage),
            request.TargetModelId.ToString(),
            before: null,
            after: new { SourceModelId = sourceModelId, TargetModelId = request.TargetModelId, CopiedCount = sourceStages.Count, request.Note },
            requestMeta: requestMeta);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

}
