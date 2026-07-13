using ProductionLinePlanner.Api.Authorization;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Api.Endpoints;

public static class ProductionCostRecordingEndpoints
{
    public static void MapProductionCostRecordingEndpoints(this WebApplication app)
    {
        var lookups = app.MapGroup("/api/production/lookups").RequireAuthorization().WithTags("Production lookups");
        lookups.MapGet("/models", async (AppDbContext db, CancellationToken ct) => Results.Ok(ApiResponse.Success(new { items = await db.ProductModels.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).Select(x => new { x.Id, x.Code, x.Name, x.IsActive, x.Description }).ToArrayAsync(ct) }))).RequirePermission("production.record");
        lookups.MapGet("/workers", async (AppDbContext db, CancellationToken ct) => Results.Ok(ApiResponse.Success(new { items = await db.Workers.AsNoTracking().Where(x => x.IsActive && x.EmploymentStatus != EmploymentStatus.LeftEmployment).OrderBy(x => x.EmployeeCode).Select(x => new { x.Id, x.EmployeeCode, x.FullName, EmploymentStatus = x.EmploymentStatus.ToString(), x.IsActive }).ToArrayAsync(ct) }))).RequirePermission("production.record");
        lookups.MapGet("/models/{modelId:guid}/stages", async (Guid modelId, AppDbContext db, CancellationToken ct) => Results.Ok(ApiResponse.Success(await db.ProductModelStages.AsNoTracking().Where(x => x.ProductModelId == modelId && x.IsActive).Include(x => x.SubStage).OrderBy(x => x.StageOrder).Select(x => new { x.Id, x.SubStageId, SubStageCode = x.SubStage!.Code, SubStageName = x.SubStage.Name, x.StageOrder, x.PiecePrice, x.StandardSeconds, CompensationMode = x.CompensationMode.ToString(), x.IsRequired, x.IsActive }).ToArrayAsync(ct)))).RequirePermission("production.record");
        var orders = app.MapGroup("/api/production/orders").RequireAuthorization().WithTags("Production orders");
        orders.MapGet("", async (IProductionCostRecordingService service, ProductionOrderStatus? status, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.ListOrdersAsync(status, ct)))).RequirePermission("production.view");
        orders.MapPost("", async (CreateProductionOrderRequest request, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) => Results.Created("/api/production/orders", ApiResponse.Success(await service.CreateOrderAsync(request, RequireUser(user), ct)))).RequirePermission("production.record");
        orders.MapPut("/{id:guid}", async (Guid id, UpdateProductionOrderRequest request, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.UpdateOrderAsync(id, request, RequireUser(user), ct)))).RequirePermission("production.record");
        orders.MapPost("/{id:guid}/activate", async (Guid id, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.TransitionOrderAsync(id, ProductionOrderStatus.Active, RequireUser(user), ct)))).RequirePermission("production.record");
        orders.MapPost("/{id:guid}/complete", async (Guid id, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.TransitionOrderAsync(id, ProductionOrderStatus.Completed, RequireUser(user), ct)))).RequirePermission("production.record");
        orders.MapPost("/{id:guid}/cancel", async (Guid id, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.TransitionOrderAsync(id, ProductionOrderStatus.Cancelled, RequireUser(user), ct)))).RequirePermission("production.approve");
        var records = app.MapGroup("/api/production/records").RequireAuthorization().WithTags("Production recording");
        records.MapGet("", async (IProductionCostRecordingService service, DateOnly? from, DateOnly? to, StageProductionRecordStatus? status, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.ListRecordsAsync(from, to, status, ct)))).RequirePermission("production.view");
        records.MapGet("/{id:guid}", async (Guid id, IProductionCostRecordingService service, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.GetRecordAsync(id, ct)))).RequirePermission("production.view");
        records.MapPost("", async (CreateStageProductionRecordRequest request, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) => Results.Created("/api/production/records", ApiResponse.Success(await service.CreateDraftAsync(request, RequireUser(user), ct)))).RequirePermission("production.record");
        records.MapPost("/preview", async (CreateStageProductionRecordRequest request, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.CalculatePreviewAsync(request, RequireUser(user), ct)))).RequirePermission("production.record");
        records.MapPut("/{id:guid}", async (Guid id, UpdateStageProductionRecordRequest request, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.UpdateDraftAsync(id, request, RequireUser(user), ct)))).RequirePermission("production.record");
        records.MapPost("/{id:guid}/approve", async (Guid id, RecordActionRequest request, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.ApproveAsync(id, request.ConcurrencyToken, RequireUser(user), ct)))).RequirePermission("production.approve");
        records.MapPost("/{id:guid}/cancel", async (Guid id, RecordActionRequest request, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.CancelAsync(id, request.ConcurrencyToken, RequireUser(user), ct)))).RequirePermission("production.approve");
        app.MapGet("/api/production/reports/daily", async (IProductionCostRecordingService service, DateOnly from, DateOnly to, Guid? orderId, Guid? modelId, Guid? workerId, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.DailyReportAsync(from, to, orderId, modelId, workerId, ct)))).RequireAuthorization().RequirePermission("production.view").WithTags("Production reports");
    }
    private static Guid RequireUser(ICurrentUserService user) => user.UserId ?? throw new UnauthorizedAccessException("User context is required.");
}
