using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Infrastructure;
using ProductionLinePlanner.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "ProductionLinePlannerCors",
        policy =>
        {
            policy
                .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
});

builder.Services.AddInfrastructure(builder.Configuration);

// Authentication placeholder: implementation will be added in a dedicated auth slice later.
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

// SignalR placeholder registration, no hub implementation details yet.
builder.Services.AddSignalR();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("ProductionLinePlannerCors");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    timestampUtc = DateTime.UtcNow
}))
    .WithTags("System")
    .WithName("Health");

app.MapGet("/", () => Results.Ok("ProductionLinePlanner API is running."))
    .WithTags("System")
    .WithName("Root");

app.MapGet("/api/identity/placeholder", () => Results.Ok(new
{
    message = "Authentication is currently a placeholder.",
    note = "JWT authentication handlers will be implemented in a future sprint."
}))
    .WithTags("Identity")
    .WithName("IdentityPlaceholder");

app.MapHub<ProductionHub>("/hubs/production");

app.MapGet("/api/factories", async (
    AppDbContext dbContext,
    CancellationToken cancellationToken,
    bool? isActive = true,
    int page = 1,
    int pageSize = 50) =>
{
    if (page < 1 || pageSize < 1 || pageSize > 200)
    {
        return ApiResponse.Failure("ValidationError", "page and pageSize must be positive, pageSize max 200.");
    }

    var query = dbContext.Factories.AsNoTracking();
    if (isActive.HasValue)
    {
        query = query.Where(x => x.IsActive == isActive.Value);
    }

    var totalCount = await query.CountAsync(cancellationToken);
    var entities = await query
        .OrderBy(x => x.Code)
        .ThenBy(x => x.Name)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(x => new FactoryDto
        {
            Id = x.Id,
            Name = x.Name,
            Code = x.Code,
            Location = x.Location,
            IsActive = x.IsActive
        })
        .ToArrayAsync(cancellationToken);

    return Results.Ok(new { success = true, data = new { items = entities, totalCount, pageNumber = page, pageSize } });
})
    .WithTags("Factories")
    .WithName("GetFactories");

app.MapGet("/api/factories/{factoryId:guid}", async (
    Guid factoryId,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.Factories
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == factoryId && x.IsActive, cancellationToken);

    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Factory not found.", statusCode: 404);
    }

    return Results.Ok(ApiResponse.Success(new FactoryDto
    {
        Id = entity.Id,
        Name = entity.Name,
        Code = entity.Code,
        Location = entity.Location,
        IsActive = entity.IsActive
    }));
})
    .WithTags("Factories")
    .WithName("GetFactory");

app.MapPost("/api/factories", async (
    CreateFactoryRequest request,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return ApiResponse.Failure("ValidationError", "Name is required.");
    }

    var code = request.Code?.Trim();
    if (string.IsNullOrWhiteSpace(code))
    {
        return ApiResponse.Failure("ValidationError", "Code is required.");
    }

    var hasConflict = await dbContext.Factories.AnyAsync(
        x => x.Code == code,
        cancellationToken);

    if (hasConflict)
    {
        return ApiResponse.Failure("Conflict", "A factory with this code already exists.", statusCode: 409);
    }

    var entity = new Factory(
        id: Guid.NewGuid(),
        name: request.Name,
        code: code,
        location: request.Location?.Trim(),
        isActive: request.IsActive);

    dbContext.Factories.Add(entity);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/factories/{entity.Id}", ApiResponse.Success(new FactoryDto
    {
        Id = entity.Id,
        Name = entity.Name,
        Code = entity.Code,
        Location = entity.Location,
        IsActive = entity.IsActive
    }));
})
    .WithTags("Factories")
    .WithName("CreateFactory");

app.MapPatch("/api/factories/{factoryId:guid}", async (
    Guid factoryId,
    UpdateFactoryRequest request,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.Factories.FirstOrDefaultAsync(x => x.Id == factoryId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Factory not found.", statusCode: 404);
    }

    if (request.Name is null && request.Location is null && request.IsActive is null)
    {
        return ApiResponse.Failure("ValidationError", "No updatable fields were provided.");
    }

    var updatedAt = DateTime.UtcNow;
    var entry = dbContext.Entry(entity);
    var changed = false;

    if (request.Name is { } name && !string.IsNullOrWhiteSpace(name))
    {
        entry.Property(nameof(Factory.Name)).CurrentValue = name.Trim();
        changed = true;
    }
    else if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
    {
        return ApiResponse.Failure("ValidationError", "Name cannot be empty.");
    }

    if (request.Location is not null)
    {
        var trimmedLocation = request.Location.Trim();
        if (string.IsNullOrWhiteSpace(trimmedLocation))
        {
            return ApiResponse.Failure("ValidationError", "Location cannot be empty.");
        }

        entry.Property(nameof(Factory.Location)).CurrentValue = trimmedLocation;
        changed = true;
    }

    if (request.IsActive is not null && entity.IsActive != request.IsActive.Value)
    {
        entry.Property(nameof(Factory.IsActive)).CurrentValue = request.IsActive.Value;
        changed = true;
    }

    if (!changed)
    {
        return ApiResponse.Failure("ValidationError", "No valid changes detected.");
    }

    entry.Property(nameof(Factory.UpdatedAtUtc)).CurrentValue = updatedAt;
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(ApiResponse.Success(new FactoryDto
    {
        Id = entity.Id,
        Name = entity.Name,
        Code = entity.Code,
        Location = entity.Location,
        IsActive = entity.IsActive
    }));
})
    .WithTags("Factories")
    .WithName("UpdateFactory");

app.MapDelete("/api/factories/{factoryId:guid}", async (
    Guid factoryId,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.Factories.FirstOrDefaultAsync(x => x.Id == factoryId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Factory not found.", statusCode: 404);
    }

    dbContext.Entry(entity).Property(nameof(Factory.IsActive)).CurrentValue = false;
    dbContext.Entry(entity).Property(nameof(Factory.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
})
    .WithTags("Factories")
    .WithName("DeleteFactory");

app.MapGet("/api/factories/{factoryId:guid}/production-lines", async (
    AppDbContext dbContext,
    Guid factoryId,
    CancellationToken cancellationToken,
    bool includeInactive = false,
    int page = 1,
    int pageSize = 50) =>
{
    if (page < 1 || pageSize < 1 || pageSize > 200)
    {
        return ApiResponse.Failure("ValidationError", "page and pageSize must be positive, pageSize max 200.");
    }

    var factoryExists = await dbContext.Factories.AnyAsync(x => x.Id == factoryId && x.IsActive, cancellationToken);
    if (!factoryExists)
    {
        return ApiResponse.Failure("NotFound", "Factory not found.", statusCode: 404);
    }

    var query = dbContext.ProductionLines
        .AsNoTracking()
        .Where(x => x.FactoryId == factoryId);

    if (!includeInactive)
    {
        query = query.Where(x => x.IsActive);
    }

    var totalCount = await query.CountAsync(cancellationToken);
    var entities = await query
        .OrderBy(x => x.SequenceOrder)
        .ThenBy(x => x.Name)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(x => new ProductionLineDto
        {
            Id = x.Id,
            FactoryId = x.FactoryId,
            Name = x.Name,
            LineCode = x.LineCode,
            SequenceOrder = x.SequenceOrder,
            IsActive = x.IsActive
        })
        .ToArrayAsync(cancellationToken);

    return Results.Ok(new { success = true, data = new { items = entities, totalCount, pageNumber = page, pageSize } });
})
    .WithTags("ProductionLines")
    .WithName("GetProductionLinesByFactory");

app.MapGet("/api/production-lines/{lineId:guid}", async (
    Guid lineId,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.ProductionLines
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == lineId && x.IsActive, cancellationToken);

    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Production line not found.", statusCode: 404);
    }

    return Results.Ok(ApiResponse.Success(new ProductionLineDto
    {
        Id = entity.Id,
        FactoryId = entity.FactoryId,
        Name = entity.Name,
        LineCode = entity.LineCode,
        SequenceOrder = entity.SequenceOrder,
        IsActive = entity.IsActive
    }));
})
    .WithTags("ProductionLines")
    .WithName("GetProductionLine");

app.MapPost("/api/production-lines", async (
    CreateProductionLineRequest request,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (request.FactoryId == Guid.Empty)
    {
        return ApiResponse.Failure("ValidationError", "FactoryId is required.");
    }

    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return ApiResponse.Failure("ValidationError", "Name is required.");
    }

    if (request.SequenceOrder < 0)
    {
        return ApiResponse.Failure("ValidationError", "SequenceOrder must be zero or greater.");
    }

    var factoryExists = await dbContext.Factories.AnyAsync(x => x.Id == request.FactoryId && x.IsActive, cancellationToken);
    if (!factoryExists)
    {
        return ApiResponse.Failure("ValidationError", "FactoryId does not exist.", 404);
    }

    var lineCode = string.IsNullOrWhiteSpace(request.LineCode) ? null : request.LineCode.Trim();
    if (lineCode is not null)
    {
        var hasDuplicateLineCode = await dbContext.ProductionLines.AnyAsync(
            x => x.FactoryId == request.FactoryId && x.LineCode == lineCode,
            cancellationToken);
        if (hasDuplicateLineCode)
        {
            return ApiResponse.Failure("Conflict", "LineCode must be unique within the factory.", statusCode: 409);
        }
    }

    var entity = new ProductionLine(
        id: Guid.NewGuid(),
        factoryId: request.FactoryId,
        name: request.Name,
        lineCode: lineCode,
        sequenceOrder: request.SequenceOrder,
        isActive: request.IsActive);

    dbContext.ProductionLines.Add(entity);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/production-lines/{entity.Id}", ApiResponse.Success(new ProductionLineDto
    {
        Id = entity.Id,
        FactoryId = entity.FactoryId,
        Name = entity.Name,
        LineCode = entity.LineCode,
        SequenceOrder = entity.SequenceOrder,
        IsActive = entity.IsActive
    }));
})
    .WithTags("ProductionLines")
    .WithName("CreateProductionLine");

app.MapPatch("/api/production-lines/{lineId:guid}", async (
    Guid lineId,
    UpdateProductionLineRequest request,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.ProductionLines.FirstOrDefaultAsync(x => x.Id == lineId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Production line not found.", statusCode: 404);
    }

    if (request.Name is null && request.LineCode is null && request.SequenceOrder is null && request.IsActive is null)
    {
        return ApiResponse.Failure("ValidationError", "No updatable fields were provided.");
    }

    var updatedAt = DateTime.UtcNow;
    var hasChanges = false;
    var entry = dbContext.Entry(entity);
    if (request.Name is { } name && !string.IsNullOrWhiteSpace(name))
    {
        entry.Property(nameof(ProductionLine.Name)).CurrentValue = name.Trim();
        hasChanges = true;
    }
    else if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
    {
        return ApiResponse.Failure("ValidationError", "Name cannot be empty.");
    }

    if (request.LineCode is not null)
    {
        var normalizedLineCode = request.LineCode.Trim();
        if (string.IsNullOrWhiteSpace(normalizedLineCode))
        {
            return ApiResponse.Failure("ValidationError", "LineCode cannot be empty.");
        }

        if (entity.LineCode != normalizedLineCode)
        {
            var conflict = await dbContext.ProductionLines.AnyAsync(
                x => x.Id != lineId && x.FactoryId == entity.FactoryId && x.LineCode == normalizedLineCode,
                cancellationToken);
            if (conflict)
            {
                return ApiResponse.Failure("Conflict", "LineCode must be unique within the factory.", 409);
            }
        }

        entry.Property(nameof(ProductionLine.LineCode)).CurrentValue = normalizedLineCode;
        hasChanges = true;
    }

    if (request.SequenceOrder is not null)
    {
        if (request.SequenceOrder.Value < 0)
        {
            return ApiResponse.Failure("ValidationError", "SequenceOrder must be zero or greater.");
        }

        if (entity.SequenceOrder != request.SequenceOrder.Value)
        {
            entity.SetSequenceOrder(request.SequenceOrder.Value, updatedAt);
            hasChanges = true;
        }
    }

    if (request.IsActive is not null)
    {
        if (entity.IsActive != request.IsActive.Value)
        {
            entry.Property(nameof(ProductionLine.IsActive)).CurrentValue = request.IsActive.Value;
            hasChanges = true;
        }
    }

    if (!hasChanges)
    {
        return ApiResponse.Failure("ValidationError", "No valid changes detected.");
    }

    entry.Property(nameof(ProductionLine.UpdatedAtUtc)).CurrentValue = updatedAt;
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(ApiResponse.Success(new ProductionLineDto
    {
        Id = entity.Id,
        FactoryId = entity.FactoryId,
        Name = entity.Name,
        LineCode = entity.LineCode,
        SequenceOrder = entity.SequenceOrder,
        IsActive = entity.IsActive
    }));
})
    .WithTags("ProductionLines")
    .WithName("UpdateProductionLine");

app.MapDelete("/api/production-lines/{lineId:guid}", async (
    Guid lineId,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.ProductionLines.FirstOrDefaultAsync(x => x.Id == lineId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Production line not found.", 404);
    }

    dbContext.Entry(entity).Property(nameof(ProductionLine.IsActive)).CurrentValue = false;
    dbContext.Entry(entity).Property(nameof(ProductionLine.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
})
    .WithTags("ProductionLines")
    .WithName("DeleteProductionLine");

app.MapGet("/api/production-lines/{productionLineId:guid}/main-stages", async (
    AppDbContext dbContext,
    Guid productionLineId,
    CancellationToken cancellationToken,
    bool includeInactive = false,
    int page = 1,
    int pageSize = 50) =>
{
    if (page < 1 || pageSize < 1 || pageSize > 200)
    {
        return ApiResponse.Failure("ValidationError", "page and pageSize must be positive, pageSize max 200.");
    }

    var lineExists = await dbContext.ProductionLines.AnyAsync(x => x.Id == productionLineId && x.IsActive, cancellationToken);
    if (!lineExists)
    {
        return ApiResponse.Failure("NotFound", "Production line not found.", 404);
    }

    var query = dbContext.MainStages.AsNoTracking().Where(x => x.ProductionLineId == productionLineId);
    if (!includeInactive)
    {
        query = query.Where(x => x.IsActive);
    }

    var totalCount = await query.CountAsync(cancellationToken);
    var entities = await query
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

    return Results.Ok(new { success = true, data = new { items = entities, totalCount, pageNumber = page, pageSize } });
})
    .WithTags("MainStages")
    .WithName("GetMainStagesByLine");

app.MapGet("/api/main-stages/{mainStageId:guid}", async (
    Guid mainStageId,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.MainStages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == mainStageId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Main stage not found.", 404);
    }

    return Results.Ok(ApiResponse.Success(new MainStageDto
    {
        Id = entity.Id,
        ProductionLineId = entity.ProductionLineId,
        Name = entity.Name,
        SequenceOrder = entity.SequenceOrder,
        IsCritical = entity.IsCritical,
        IsActive = entity.IsActive
    }));
})
    .WithTags("MainStages")
    .WithName("GetMainStage");

app.MapPost("/api/main-stages", async (
    CreateMainStageRequest request,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (request.ProductionLineId == Guid.Empty)
    {
        return ApiResponse.Failure("ValidationError", "ProductionLineId is required.");
    }

    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return ApiResponse.Failure("ValidationError", "Name is required.");
    }

    if (request.SequenceOrder < 0)
    {
        return ApiResponse.Failure("ValidationError", "SequenceOrder must be zero or greater.");
    }

    var lineExists = await dbContext.ProductionLines.AnyAsync(x => x.Id == request.ProductionLineId && x.IsActive, cancellationToken);
    if (!lineExists)
    {
        return ApiResponse.Failure("ValidationError", "ProductionLineId does not exist.", 404);
    }

    var hasConflict = await dbContext.MainStages.AnyAsync(
        x => x.ProductionLineId == request.ProductionLineId && x.SequenceOrder == request.SequenceOrder && x.IsActive,
        cancellationToken);
    if (hasConflict)
    {
        return ApiResponse.Failure("Conflict", "SequenceOrder must be unique for this production line.", statusCode: 409);
    }

    var entity = new MainStage(
        id: Guid.NewGuid(),
        productionLineId: request.ProductionLineId,
        name: request.Name,
        isCritical: request.IsCritical,
        sequenceOrder: request.SequenceOrder,
        isActive: request.IsActive);

    dbContext.MainStages.Add(entity);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/main-stages/{entity.Id}", ApiResponse.Success(new MainStageDto
    {
        Id = entity.Id,
        ProductionLineId = entity.ProductionLineId,
        Name = entity.Name,
        SequenceOrder = entity.SequenceOrder,
        IsCritical = entity.IsCritical,
        IsActive = entity.IsActive
    }));
})
    .WithTags("MainStages")
    .WithName("CreateMainStage");

app.MapPatch("/api/main-stages/{mainStageId:guid}", async (
    Guid mainStageId,
    UpdateMainStageRequest request,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.MainStages.FirstOrDefaultAsync(x => x.Id == mainStageId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Main stage not found.", 404);
    }

    if (request.Name is null && request.IsCritical is null && request.SequenceOrder is null && request.IsActive is null)
    {
        return ApiResponse.Failure("ValidationError", "No updatable fields were provided.");
    }

    var updatedAt = DateTime.UtcNow;
    var hasChanges = false;
    if (request.Name is { } name && !string.IsNullOrWhiteSpace(name))
    {
        entity.Rename(name, updatedAt);
        hasChanges = true;
    }
    else if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
    {
        return ApiResponse.Failure("ValidationError", "Name cannot be empty.");
    }

    if (request.IsCritical is not null)
    {
        dbContext.Entry(entity).Property(nameof(MainStage.IsCritical)).CurrentValue = request.IsCritical.Value;
        hasChanges = true;
    }

    if (request.SequenceOrder is not null)
    {
        if (request.SequenceOrder.Value < 0)
        {
            return ApiResponse.Failure("ValidationError", "SequenceOrder must be zero or greater.");
        }

        if (entity.SequenceOrder != request.SequenceOrder.Value)
        {
            var sequenceConflict = await dbContext.MainStages.AnyAsync(
                x => x.Id != mainStageId && x.ProductionLineId == entity.ProductionLineId && x.SequenceOrder == request.SequenceOrder.Value,
                cancellationToken);
            if (sequenceConflict)
            {
                return ApiResponse.Failure("Conflict", "SequenceOrder must be unique within this production line.", 409);
            }

            dbContext.Entry(entity).Property(nameof(MainStage.SequenceOrder)).CurrentValue = request.SequenceOrder.Value;
            hasChanges = true;
        }
    }

    if (request.IsActive is not null)
    {
        if (entity.IsActive != request.IsActive.Value)
        {
            dbContext.Entry(entity).Property(nameof(MainStage.IsActive)).CurrentValue = request.IsActive.Value;
            hasChanges = true;
        }
    }

    if (!hasChanges)
    {
        return ApiResponse.Failure("ValidationError", "No valid changes detected.");
    }

    dbContext.Entry(entity).Property(nameof(MainStage.UpdatedAtUtc)).CurrentValue = updatedAt;
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(ApiResponse.Success(new MainStageDto
    {
        Id = entity.Id,
        ProductionLineId = entity.ProductionLineId,
        Name = entity.Name,
        SequenceOrder = entity.SequenceOrder,
        IsCritical = entity.IsCritical,
        IsActive = entity.IsActive
    }));
})
    .WithTags("MainStages")
    .WithName("UpdateMainStage");

app.MapDelete("/api/main-stages/{mainStageId:guid}", async (
    Guid mainStageId,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.MainStages.FirstOrDefaultAsync(x => x.Id == mainStageId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Main stage not found.", 404);
    }

    dbContext.Entry(entity).Property(nameof(MainStage.IsActive)).CurrentValue = false;
    dbContext.Entry(entity).Property(nameof(MainStage.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
})
    .WithTags("MainStages")
    .WithName("DeleteMainStage");

app.MapGet("/api/main-stages/{mainStageId:guid}/sub-stages", async (
    AppDbContext dbContext,
    Guid mainStageId,
    CancellationToken cancellationToken,
    bool includeInactive = false,
    int page = 1,
    int pageSize = 50) =>
{
    if (page < 1 || pageSize < 1 || pageSize > 200)
    {
        return ApiResponse.Failure("ValidationError", "page and pageSize must be positive, pageSize max 200.");
    }

    var mainStageExists = await dbContext.MainStages.AnyAsync(x => x.Id == mainStageId && x.IsActive, cancellationToken);
    if (!mainStageExists)
    {
        return ApiResponse.Failure("NotFound", "Main stage not found.", 404);
    }

    var query = dbContext.SubStages.AsNoTracking().Where(x => x.MainStageId == mainStageId);
    if (!includeInactive)
    {
        query = query.Where(x => x.IsActive);
    }

    var totalCount = await query.CountAsync(cancellationToken);
    var entities = await query
        .OrderBy(x => x.SequenceOrder)
        .ThenBy(x => x.Name)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(x => new SubStageDto
        {
            Id = x.Id,
            MainStageId = x.MainStageId,
            Name = x.Name,
            Capacity = x.Capacity,
            SequenceOrder = x.SequenceOrder,
            IsActive = x.IsActive
        })
        .ToArrayAsync(cancellationToken);

    return Results.Ok(new { success = true, data = new { items = entities, totalCount, pageNumber = page, pageSize } });
})
    .WithTags("SubStages")
    .WithName("GetSubStagesByMainStage");

app.MapGet("/api/sub-stages/{subStageId:guid}", async (
    Guid subStageId,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.SubStages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == subStageId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Sub stage not found.", 404);
    }

    return Results.Ok(ApiResponse.Success(new SubStageDto
    {
        Id = entity.Id,
        MainStageId = entity.MainStageId,
        Name = entity.Name,
        Capacity = entity.Capacity,
        SequenceOrder = entity.SequenceOrder,
        IsActive = entity.IsActive
    }));
})
    .WithTags("SubStages")
    .WithName("GetSubStage");

app.MapPost("/api/sub-stages", async (
    CreateSubStageRequest request,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (request.MainStageId == Guid.Empty)
    {
        return ApiResponse.Failure("ValidationError", "MainStageId is required.");
    }

    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return ApiResponse.Failure("ValidationError", "Name is required.");
    }

    if (request.Capacity < 0)
    {
        return ApiResponse.Failure("ValidationError", "Capacity must be zero or greater.");
    }

    if (request.SequenceOrder < 0)
    {
        return ApiResponse.Failure("ValidationError", "SequenceOrder must be zero or greater.");
    }

    var mainStageExists = await dbContext.MainStages.AnyAsync(x => x.Id == request.MainStageId && x.IsActive, cancellationToken);
    if (!mainStageExists)
    {
        return ApiResponse.Failure("ValidationError", "MainStageId does not exist.", 404);
    }

    var hasConflict = await dbContext.SubStages.AnyAsync(
        x => x.MainStageId == request.MainStageId && x.SequenceOrder == request.SequenceOrder && x.IsActive,
        cancellationToken);
    if (hasConflict)
    {
        return ApiResponse.Failure("Conflict", "SequenceOrder must be unique within this main stage.", 409);
    }

    var entity = new SubStage(
        id: Guid.NewGuid(),
        mainStageId: request.MainStageId,
        name: request.Name,
        capacity: request.Capacity,
        sequenceOrder: request.SequenceOrder,
        isActive: request.IsActive);

    dbContext.SubStages.Add(entity);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/sub-stages/{entity.Id}", ApiResponse.Success(new SubStageDto
    {
        Id = entity.Id,
        MainStageId = entity.MainStageId,
        Name = entity.Name,
        Capacity = entity.Capacity,
        SequenceOrder = entity.SequenceOrder,
        IsActive = entity.IsActive
    }));
})
    .WithTags("SubStages")
    .WithName("CreateSubStage");

app.MapPatch("/api/sub-stages/{subStageId:guid}", async (
    Guid subStageId,
    UpdateSubStageRequest request,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.SubStages.FirstOrDefaultAsync(x => x.Id == subStageId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Sub stage not found.", 404);
    }

    if (request.Name is null && request.Capacity is null && request.SequenceOrder is null && request.IsActive is null)
    {
        return ApiResponse.Failure("ValidationError", "No updatable fields were provided.");
    }

    var updatedAt = DateTime.UtcNow;
    var hasChanges = false;

    if (request.Name is { } name && !string.IsNullOrWhiteSpace(name))
    {
        dbContext.Entry(entity).Property(nameof(SubStage.Name)).CurrentValue = name.Trim();
        hasChanges = true;
    }
    else if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
    {
        return ApiResponse.Failure("ValidationError", "Name cannot be empty.");
    }

    if (request.Capacity is not null)
    {
        try
        {
            entity.UpdateCapacity(request.Capacity.Value, updatedAt);
            hasChanges = true;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return ApiResponse.Failure("ValidationError", ex.Message);
        }
    }

    if (request.SequenceOrder is not null)
    {
        if (request.SequenceOrder.Value < 0)
        {
            return ApiResponse.Failure("ValidationError", "SequenceOrder must be zero or greater.");
        }

        if (entity.SequenceOrder != request.SequenceOrder.Value)
        {
            var sequenceConflict = await dbContext.SubStages.AnyAsync(
                x => x.Id != subStageId && x.MainStageId == entity.MainStageId && x.SequenceOrder == request.SequenceOrder.Value,
                cancellationToken);
            if (sequenceConflict)
            {
                return ApiResponse.Failure("Conflict", "SequenceOrder must be unique within this main stage.", 409);
            }

            dbContext.Entry(entity).Property(nameof(SubStage.SequenceOrder)).CurrentValue = request.SequenceOrder.Value;
            hasChanges = true;
        }
    }

    if (request.IsActive is not null)
    {
        if (entity.IsActive != request.IsActive.Value)
        {
            dbContext.Entry(entity).Property(nameof(SubStage.IsActive)).CurrentValue = request.IsActive.Value;
            hasChanges = true;
        }
    }

    if (!hasChanges)
    {
        return ApiResponse.Failure("ValidationError", "No valid changes detected.");
    }

    dbContext.Entry(entity).Property(nameof(SubStage.UpdatedAtUtc)).CurrentValue = updatedAt;
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(ApiResponse.Success(new SubStageDto
    {
        Id = entity.Id,
        MainStageId = entity.MainStageId,
        Name = entity.Name,
        Capacity = entity.Capacity,
        SequenceOrder = entity.SequenceOrder,
        IsActive = entity.IsActive
    }));
})
    .WithTags("SubStages")
    .WithName("UpdateSubStage");

app.MapDelete("/api/sub-stages/{subStageId:guid}", async (
    Guid subStageId,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.SubStages.FirstOrDefaultAsync(x => x.Id == subStageId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Sub stage not found.", 404);
    }

    dbContext.Entry(entity).Property(nameof(SubStage.IsActive)).CurrentValue = false;
    dbContext.Entry(entity).Property(nameof(SubStage.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
})
    .WithTags("SubStages")
    .WithName("DeleteSubStage");

app.MapGet("/api/workers", async (
    AppDbContext dbContext,
    string? search,
    CancellationToken cancellationToken,
    bool? isActive = true,
    int page = 1,
    int pageSize = 50) =>
{
    if (page < 1 || pageSize < 1 || pageSize > 200)
    {
        return ApiResponse.Failure("ValidationError", "page and pageSize must be positive, pageSize max 200.");
    }

    var searchPattern = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
    var query = dbContext.Workers.AsNoTracking();

    if (isActive.HasValue)
    {
        query = query.Where(x => x.IsActive == isActive.Value);
    }

    if (searchPattern is not null)
    {
        query = query.Where(x => EF.Functions.Like(x.EmployeeCode, searchPattern) || EF.Functions.Like(x.FullName, searchPattern));
    }

    var totalCount = await query.CountAsync(cancellationToken);
    var entities = await query
        .OrderBy(x => x.FullName)
        .ThenBy(x => x.EmployeeCode)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToArrayAsync(cancellationToken);

    var workerIds = entities.Select(x => x.Id).ToArray();
    var activeDefaultAssignments = new List<(Guid WorkerId, DateTime AssignedAt, Guid Id, Guid SubStageId)>();
    if (workerIds.Length > 0)
    {
        activeDefaultAssignments = (await dbContext.WorkerDefaultAssignments
                .AsNoTracking()
                .Where(x => workerIds.Contains(x.WorkerId) && x.IsActive)
                .Select(x => new { x.WorkerId, x.AssignedAt, x.Id, x.SubStageId })
                .ToListAsync(cancellationToken))
            .Select(x => (x.WorkerId, x.AssignedAt, x.Id, x.SubStageId))
            .ToList();
    }

    var defaultSubStageByWorker = activeDefaultAssignments
        .OrderByDescending(x => x.AssignedAt)
        .ThenByDescending(x => x.Id)
        .GroupBy(x => x.WorkerId)
        .ToDictionary(g => g.Key, g => (Guid?)g.First().SubStageId);

    var dtos = entities.Select(x => new WorkerDto
    {
        Id = x.Id,
        EmployeeCode = x.EmployeeCode,
        FullName = x.FullName,
        AttendanceUserId = x.AttendanceUserId,
        BadgeNumber = x.BadgeNumber,
        Phone = x.Phone,
        IsActive = x.IsActive,
        DefaultSubStageId = defaultSubStageByWorker.GetValueOrDefault(x.Id)
    }).ToArray();

    return Results.Ok(new { success = true, data = new { items = dtos, totalCount, pageNumber = page, pageSize } });
})
    .WithTags("Workers")
    .WithName("GetWorkers");

app.MapGet("/api/workers/{workerId:guid}", async (
    Guid workerId,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.Workers
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == workerId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Worker not found.", 404);
    }

    var defaultSubStageId = await dbContext.WorkerDefaultAssignments
        .AsNoTracking()
        .Where(x => x.WorkerId == workerId && x.IsActive)
        .OrderByDescending(x => x.AssignedAt)
        .ThenByDescending(x => x.Id)
        .Select(x => (Guid?)x.SubStageId)
        .FirstOrDefaultAsync(cancellationToken);

    return Results.Ok(ApiResponse.Success(new WorkerDto
    {
        Id = entity.Id,
        EmployeeCode = entity.EmployeeCode,
        FullName = entity.FullName,
        AttendanceUserId = entity.AttendanceUserId,
        BadgeNumber = entity.BadgeNumber,
        Phone = entity.Phone,
        IsActive = entity.IsActive,
        DefaultSubStageId = defaultSubStageId
    }));
})
    .WithTags("Workers")
    .WithName("GetWorker");

app.MapPost("/api/workers", async (
    CreateWorkerRequest request,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.EmployeeCode))
    {
        return ApiResponse.Failure("ValidationError", "EmployeeCode is required.");
    }

    if (string.IsNullOrWhiteSpace(request.FullName))
    {
        return ApiResponse.Failure("ValidationError", "FullName is required.");
    }

    var employeeCode = request.EmployeeCode.Trim();
    var hasConflict = await dbContext.Workers.AnyAsync(x => x.EmployeeCode == employeeCode, cancellationToken);
    if (hasConflict)
    {
        return ApiResponse.Failure("Conflict", "EmployeeCode must be unique.", 409);
    }

    var entity = new Worker(
        id: Guid.NewGuid(),
        employeeCode: employeeCode,
        fullName: request.FullName,
        phone: request.Phone,
        attendanceUserId: request.AttendanceUserId,
        badgeNumber: request.BadgeNumber,
        isActive: request.IsActive);

    dbContext.Workers.Add(entity);
    await dbContext.SaveChangesAsync(cancellationToken);

    var defaultSubStageId = await dbContext.WorkerDefaultAssignments
        .AsNoTracking()
        .Where(x => x.WorkerId == entity.Id && x.IsActive)
        .OrderByDescending(x => x.AssignedAt)
        .ThenByDescending(x => x.Id)
        .Select(x => (Guid?)x.SubStageId)
        .FirstOrDefaultAsync(cancellationToken);

    return Results.Created($"/api/workers/{entity.Id}", ApiResponse.Success(new WorkerDto
    {
        Id = entity.Id,
        EmployeeCode = entity.EmployeeCode,
        FullName = entity.FullName,
        AttendanceUserId = entity.AttendanceUserId,
        BadgeNumber = entity.BadgeNumber,
        Phone = entity.Phone,
        IsActive = entity.IsActive,
        DefaultSubStageId = defaultSubStageId
    }));
})
    .WithTags("Workers")
    .WithName("CreateWorker");

app.MapPatch("/api/workers/{workerId:guid}", async (
    Guid workerId,
    UpdateWorkerRequest request,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.Workers.FirstOrDefaultAsync(x => x.Id == workerId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Worker not found.", 404);
    }

    if (request.FullName is null && request.Phone is null && request.AttendanceUserId is null &&
        request.BadgeNumber is null && request.IsActive is null)
    {
        return ApiResponse.Failure("ValidationError", "No updatable fields were provided.");
    }

    var updatedAt = DateTime.UtcNow;
    var hasChanges = false;

    if (request.FullName is { } fullName && !string.IsNullOrWhiteSpace(fullName))
    {
        entity.UpdateName(fullName, updatedAt);
        hasChanges = true;
    }
    else if (request.FullName is not null && string.IsNullOrWhiteSpace(request.FullName))
    {
        return ApiResponse.Failure("ValidationError", "FullName cannot be empty.");
    }

    if (request.AttendanceUserId is not null)
    {
        var trimmedAttendanceUserId = request.AttendanceUserId.Trim();
        if (string.IsNullOrWhiteSpace(trimmedAttendanceUserId))
        {
            return ApiResponse.Failure("ValidationError", "AttendanceUserId cannot be empty.");
        }

        if (entity.AttendanceUserId != trimmedAttendanceUserId)
        {
            dbContext.Entry(entity).Property(nameof(Worker.AttendanceUserId)).CurrentValue = trimmedAttendanceUserId;
            hasChanges = true;
        }
    }

    if (request.Phone is not null)
    {
        var trimmedPhone = request.Phone.Trim();
        if (string.IsNullOrWhiteSpace(trimmedPhone))
        {
            return ApiResponse.Failure("ValidationError", "Phone cannot be empty.");
        }

        if (entity.Phone != trimmedPhone)
        {
            dbContext.Entry(entity).Property(nameof(Worker.Phone)).CurrentValue = trimmedPhone;
            hasChanges = true;
        }
    }

    if (request.BadgeNumber is not null)
    {
        var trimmedBadgeNumber = request.BadgeNumber.Trim();
        if (string.IsNullOrWhiteSpace(trimmedBadgeNumber))
        {
            return ApiResponse.Failure("ValidationError", "BadgeNumber cannot be empty.");
        }

        if (entity.BadgeNumber != trimmedBadgeNumber)
        {
            dbContext.Entry(entity).Property(nameof(Worker.BadgeNumber)).CurrentValue = trimmedBadgeNumber;
            hasChanges = true;
        }
    }

    if (request.IsActive is not null)
    {
        if (entity.IsActive != request.IsActive.Value)
        {
            dbContext.Entry(entity).Property(nameof(Worker.IsActive)).CurrentValue = request.IsActive.Value;
            hasChanges = true;
        }
    }

    if (!hasChanges)
    {
        return ApiResponse.Failure("ValidationError", "No valid changes detected.");
    }

    dbContext.Entry(entity).Property(nameof(Worker.UpdatedAtUtc)).CurrentValue = updatedAt;
    await dbContext.SaveChangesAsync(cancellationToken);

    var defaultSubStageId = await dbContext.WorkerDefaultAssignments
        .AsNoTracking()
        .Where(x => x.WorkerId == workerId && x.IsActive)
        .OrderByDescending(x => x.AssignedAt)
        .ThenByDescending(x => x.Id)
        .Select(x => (Guid?)x.SubStageId)
        .FirstOrDefaultAsync(cancellationToken);

    return Results.Ok(ApiResponse.Success(new WorkerDto
    {
        Id = entity.Id,
        EmployeeCode = entity.EmployeeCode,
        FullName = entity.FullName,
        AttendanceUserId = entity.AttendanceUserId,
        BadgeNumber = entity.BadgeNumber,
        Phone = entity.Phone,
        IsActive = entity.IsActive,
        DefaultSubStageId = defaultSubStageId
    }));
})
    .WithTags("Workers")
    .WithName("UpdateWorker");

app.MapDelete("/api/workers/{workerId:guid}", async (
    Guid workerId,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entity = await dbContext.Workers.FirstOrDefaultAsync(x => x.Id == workerId && x.IsActive, cancellationToken);
    if (entity is null)
    {
        return ApiResponse.Failure("NotFound", "Worker not found.", 404);
    }

    dbContext.Entry(entity).Property(nameof(Worker.IsActive)).CurrentValue = false;
    dbContext.Entry(entity).Property(nameof(Worker.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
})
    .WithTags("Workers")
    .WithName("DeleteWorker");

app.Run();

public static class ApiResponse
{
    public static IResult Failure(string code, string message, int statusCode = 400)
    {
        return Results.Json(
            new { success = false, error = new { code, message } },
            statusCode: statusCode);
    }

    public static object Success(object? data, string message = "OK") => new { success = true, message, data };
}

public sealed class ProductionHub : Hub
{
}
