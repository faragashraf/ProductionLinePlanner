using ProductionLinePlanner.Api.Authorization;
using ProductionLinePlanner.Api.Security;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;
using System.Text.Json;

namespace ProductionLinePlanner.Api.Endpoints;

public static class ProductionCostRecordingEndpoints
{
    private const string DraftPreviewRoute = "/preview";

    public static void MapProductionCostRecordingEndpoints(this WebApplication app)
    {
        var lookups = app.MapGroup("/api/production/lookups").RequireAuthorization().WithTags("Production lookups");
        lookups.MapGet("/models", async (AppDbContext db, CancellationToken ct) => Results.Ok(ApiResponse.Success(new { items = await db.ProductModels.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).Select(x => new { x.Id, x.Code, x.Name, x.IsActive, x.Description }).ToArrayAsync(ct) }))).RequirePermission("production.record").RequireRateLimiting(ApiRateLimitPolicies.NormalRead);
        lookups.MapGet("/workers", async (AppDbContext db, CancellationToken ct) => Results.Ok(ApiResponse.Success(new { items = await db.Workers.AsNoTracking().Where(x => x.IsActive && x.EmploymentStatus == EmploymentStatus.Active).OrderBy(x => x.EmployeeCode).Select(x => new { x.Id, x.EmployeeCode, x.FullName, EmploymentStatus = x.EmploymentStatus.ToString(), x.IsActive }).ToArrayAsync(ct) }))).RequirePermission("production.record").RequireRateLimiting(ApiRateLimitPolicies.NormalRead);
        lookups.MapGet("/models/{modelId:guid}/production-lines/{productionLineId:guid}/stages", async (Guid modelId, Guid productionLineId, AppDbContext db, CancellationToken ct) => Results.Ok(ApiResponse.Success(await db.ProductModelStages.AsNoTracking().Where(x => x.ProductModelId == modelId && x.ProductionLineId == productionLineId && x.IsActive).Include(x => x.SubStage).OrderBy(x => x.StageOrder).Select(x => new { x.Id, x.ProductionLineId, x.SubStageId, SubStageCode = x.SubStage!.Code, SubStageName = x.SubStage.Name, x.StageOrder, x.PiecePrice, x.StandardSeconds, CompensationMode = x.CompensationMode.ToString(), x.IsRequired, x.IsActive }).ToArrayAsync(ct)))).RequirePermission("production.record").RequireRateLimiting(ApiRateLimitPolicies.NormalRead);
        app.MapGet("/api/production/readiness", async (
            Guid productModelId,
            Guid productionLineId,
            DateOnly productionDate,
            IProductionReadinessEngine readinessEngine,
            CancellationToken ct) =>
        {
            var result = await readinessEngine.GetProductReadinessAsync(productModelId, productionLineId, productionDate, ct);
            if (result.IsFailure)
            {
                var status = result.Error?.Code == "NotFound" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
                return ApiResponse.Failure(result.Error?.Code ?? "ReadinessFailed", result.Error?.Message ?? "Unable to calculate product readiness.", status);
            }

            return Results.Ok(ApiResponse.Success(result.Value!));
        }).RequireAuthorization().RequirePermission("production.view").RequireRateLimiting(ApiRateLimitPolicies.NormalRead).WithTags("Production readiness").WithName("GetProductProductionReadiness");
        var orders = app.MapGroup("/api/production/orders").RequireAuthorization().WithTags("Production orders");
        orders.MapGet("", async (IProductionCostRecordingService service, ProductionOrderStatus? status, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.ListOrdersAsync(status, ct)))).RequirePermission("production.view").RequireRateLimiting(ApiRateLimitPolicies.NormalRead);
        orders.MapPost("", async (CreateProductionOrderRequest request, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) => Results.Created("/api/production/orders", ApiResponse.Success(await service.CreateOrderAsync(request, RequireUser(user), ct)))).RequirePermission("production.record").RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite);
        orders.MapPut("/{id:guid}", async (Guid id, UpdateProductionOrderRequest request, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.UpdateOrderAsync(id, request, RequireUser(user), ct)))).RequirePermission("production.record").RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite);
        orders.MapPost("/{id:guid}/activate", async (Guid id, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.TransitionOrderAsync(id, ProductionOrderStatus.Active, RequireUser(user), ct)))).RequirePermission("production.record").RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite);
        orders.MapPost("/{id:guid}/complete", async (Guid id, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.TransitionOrderAsync(id, ProductionOrderStatus.Completed, RequireUser(user), ct)))).RequirePermission("production.record").RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite);
        orders.MapPost("/{id:guid}/cancel", async (Guid id, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.TransitionOrderAsync(id, ProductionOrderStatus.Cancelled, RequireUser(user), ct)))).RequirePermission("production.approve").RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite);
        var records = app.MapGroup("/api/production/records").RequireAuthorization().WithTags("Production recording");
        records.MapGet("", async (IProductionCostRecordingService service, DateOnly? from, DateOnly? to, StageProductionRecordStatus? status, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.ListRecordsAsync(from, to, status, ct)))).RequirePermission("production.view").RequireRateLimiting(ApiRateLimitPolicies.NormalRead);
        records.MapGet("/{id:guid}", async (Guid id, IProductionCostRecordingService service, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.GetRecordAsync(id, ct)))).RequirePermission("production.view").RequireRateLimiting(ApiRateLimitPolicies.NormalRead);
        records.MapPost("", async (CreateStageProductionRecordRequest request, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) => Results.Created("/api/production/records", ApiResponse.Success(await service.CreateDraftAsync(request, RequireUser(user), ct)))).RequirePermission("production.record").RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite);
        records.MapPost(DraftPreviewRoute, async (CreateStageProductionRecordRequest request, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.CalculatePreviewAsync(request, RequireUser(user), ct))))
            .RequirePermission("production.record")
            .RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite)
            .WithName("CalculateStageProductionRecordPreview");
        records.MapPut("/{id:guid}", async (Guid id, UpdateStageProductionRecordRequest request, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.UpdateDraftAsync(id, request, RequireUser(user), ct)))).RequirePermission("production.record").RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite);
        records.MapPost("/{id:guid}/approve", async (Guid id, RecordActionRequest request, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.ApproveAsync(id, request.ConcurrencyToken, RequireUser(user), ct)))).RequirePermission("production.approve").RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite);
        records.MapPost("/{id:guid}/cancel-production-approval", async (Guid id, CancelProductionApprovalRequest request, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.CancelProductionApprovalAsync(id, request.ConcurrencyToken, request.Reason, RequireUser(user), ct)))).RequirePermission("production.approve").RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite);
        var dailyOperations = app.MapGroup("/api/production/daily-operations").RequireAuthorization().WithTags("Daily production operations");
        dailyOperations.MapGet("", async (
            Guid factoryId,
            Guid productionLineId,
            Guid productModelId,
            DateOnly productionDate,
            IProductionCostRecordingService service,
            CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.LoadDailyOperationsAsync(factoryId, productionLineId, productModelId, productionDate, ct))))
            .RequirePermission("production.view")
            .RequireRateLimiting(ApiRateLimitPolicies.NormalRead)
            .WithName("LoadDailyProductionOperations");
        dailyOperations.MapPost("/preview", async (
            DailyProductionOperationRequest request,
            IProductionCostRecordingService service,
            ICurrentUserService user,
            HttpContext context,
            IHostEnvironment environment,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (environment.IsDevelopment())
            {
                loggerFactory.CreateLogger("ProductionLinePlanner.Api.DailyProductionPreview").LogInformation(
                    "Daily preview payload {CorrelationId} {ClientRequestId} {StageCount} {WorkerAllocationCount}",
                    context.TraceIdentifier,
                    request.ClientRequestId,
                    request.Stages?.Count ?? 0,
                    request.Stages?.Sum(stage => stage.Workers?.Count ?? 0) ?? 0);
            }

            return Results.Ok(ApiResponse.Success(await service.PreviewDailyOperationsAsync(request, RequireUser(user), ct)));
        })
            .RequirePermission("production.record")
            // Preview is a read-only calculation. Keep it independent from the
            // stricter write bucket used by the transactional Draft save.
            .RequireRateLimiting(ApiRateLimitPolicies.NormalRead)
            .WithName("PreviewDailyProductionOperations");
        dailyOperations.MapPost("/drafts", async (DailyProductionOperationRequest request, IProductionCostRecordingService service, ICurrentUserService user, CancellationToken ct) =>
            Results.Created("/api/production/daily-operations/drafts", ApiResponse.Success(await service.CreateDailyDraftAsync(request, RequireUser(user), ct))))
            .RequirePermission("production.record")
            .RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite)
            .WithName("CreateDailyProductionDraft");
        dailyOperations.MapPut("/drafts/{productionOrderId:guid}", async (
            Guid productionOrderId,
            DailyProductionDraftUpdateRequest request,
            IProductionCostRecordingService service,
            ICurrentUserService user,
            HttpContext context,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            loggerFactory.CreateLogger("ProductionLinePlanner.Api.DailyDraftUpdate").LogDebug(
                "Daily draft PUT handler entered {TraceIdentifier} {CorrelationId} {ProductionOrderId} {StageCount} {WorkerAllocationCount}",
                context.TraceIdentifier,
                context.Request.Headers["X-Manufacturing-Realtime-Correlation-Id"].ToString(),
                productionOrderId,
                request.Stages?.Count ?? 0,
                request.Stages?.Sum(stage => stage.Workers?.Count ?? 0) ?? 0);
            if (productionOrderId == Guid.Empty)
                return ApiResponse.Failure("ValidationError", "معرّف تشغيل اليوم مطلوب.", StatusCodes.Status400BadRequest);

            return Results.Ok(ApiResponse.Success(await service.UpdateDailyDraftAsync(productionOrderId, request, RequireUser(user), ct)));
        })
            .RequirePermission("production.record")
            .RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite)
            .WithName("UpdateDailyProductionDraft");
        dailyOperations.MapPost("/{productionOrderId:guid}/approve", async (
            Guid productionOrderId,
            DailyProductionApprovalRequest request,
            IProductionCostRecordingService service,
            ICurrentUserService user,
            CancellationToken ct) =>
        {
            if (productionOrderId == Guid.Empty)
            {
                return ApiResponse.Failure("ValidationError", "معرّف تشغيل اليوم مطلوب.", StatusCodes.Status400BadRequest);
            }

            if (request.StageApprovals is null || request.StageApprovals.Count == 0 ||
                request.StageApprovals.Any(stage => stage.StageProductionRecordId == Guid.Empty || stage.ConcurrencyToken == Guid.Empty) ||
                request.StageApprovals.Select(stage => stage.StageProductionRecordId).Distinct().Count() != request.StageApprovals.Count)
            {
                return ApiResponse.Failure("ValidationError", "يجب إرسال معرّف وتزامن كل مرحلة محفوظة لاعتماد تشغيل اليوم.", StatusCodes.Status400BadRequest);
            }

            return Results.Ok(ApiResponse.Success(await service.ApproveDailyOperationAsync(productionOrderId, request, RequireUser(user), ct)));
        })
            .RequirePermission("production.approve")
            .RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite)
            .WithName("ApproveDailyProductionOperation");
        dailyOperations.MapPost("/{productionOrderId:guid}/cancel-approval", async (
            Guid productionOrderId,
            DailyProductionApprovalCancellationRequest request,
            IProductionCostRecordingService service,
            ICurrentUserService user,
            CancellationToken ct) =>
        {
            if (productionOrderId == Guid.Empty)
            {
                return ApiResponse.Failure("ValidationError", "معرّف تشغيل اليوم مطلوب.", StatusCodes.Status400BadRequest);
            }

            if (string.IsNullOrWhiteSpace(request.Reason) || request.StageApprovals is null || request.StageApprovals.Count == 0 ||
                request.StageApprovals.Any(stage => stage.StageProductionRecordId == Guid.Empty || stage.ConcurrencyToken == Guid.Empty) ||
                request.StageApprovals.Select(stage => stage.StageProductionRecordId).Distinct().Count() != request.StageApprovals.Count)
            {
                return ApiResponse.Failure("ValidationError", "سبب إلغاء الاعتماد ورموز تزامن جميع مراحل التشغيل مطلوبة.", StatusCodes.Status400BadRequest);
            }

            return Results.Ok(ApiResponse.Success(await service.CancelDailyOperationApprovalAsync(productionOrderId, request, RequireUser(user), ct)));
        })
            .RequirePermission("production.approve")
            .RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite)
            .WithName("CancelDailyProductionOperationApproval");
        // The generic workbook workflow is intentionally deferred for the first
        // real-data pilot. Keep the earlier capability dormant unless it is
        // deliberately enabled in a later release.
        if (app.Configuration.GetValue<bool>("Features:EnableDeferredGenericRealDataIntake"))
        {
            var intake = app.MapGroup("/api/production/intake").RequireAuthorization().WithTags("Controlled real-data intake");
            intake.MapPost("/preview", async (HttpRequest request, IRealDataIntakeService service, CancellationToken ct) =>
            {
                var upload = await ReadIntakeUploadAsync(request, ct);
                return Results.Ok(ApiResponse.Success(await service.PreviewAsync(upload, ct)));
            }).RequirePermission("production.record").RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite);
            intake.MapPost("/apply", async (HttpRequest request, IRealDataIntakeService service, ICurrentUserService user, CancellationToken ct) =>
            {
                var upload = await ReadIntakeUploadAsync(request, ct);
                return Results.Ok(ApiResponse.Success(await service.ApplyAsync(upload, RequireUser(user), ct)));
            }).RequirePermission("production.record").RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite);
            intake.MapGet("/days/{productionOrderId:guid}", async (Guid productionOrderId, IRealDataIntakeService service, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.GetProductionDayReviewAsync(productionOrderId, ct)))).RequirePermission("production.view").RequireRateLimiting(ApiRateLimitPolicies.NormalRead);
            intake.MapPost("/days/{productionOrderId:guid}/stages/{productModelStageId:guid}/not-operated", async (Guid productionOrderId, Guid productModelStageId, MarkStageNotOperatedRequest request, IRealDataIntakeService service, ICurrentUserService user, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.MarkStageNotOperatedAsync(productionOrderId, productModelStageId, request.Reason, RequireUser(user), ct)))).RequirePermission("production.record").RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite);
            intake.MapPost("/days/{productionOrderId:guid}/records/{stageProductionRecordId:guid}/workers/{workerId:guid}/attendance-override", async (Guid productionOrderId, Guid stageProductionRecordId, Guid workerId, ParticipantOverrideRequest request, IRealDataIntakeService service, ICurrentUserService user, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.SetParticipantOverrideAsync(productionOrderId, stageProductionRecordId, workerId, request.Reason, RequireUser(user), ct)))).RequirePermission("production.approve").RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite);
            intake.MapPost("/days/{productionOrderId:guid}/approve", async (Guid productionOrderId, IRealDataIntakeService service, ICurrentUserService user, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.ApproveProductionDayAsync(productionOrderId, RequireUser(user), ct)))).RequirePermission("production.approve").RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite);
        }
        app.MapGet("/api/production/reports/daily", async (IProductionCostRecordingService service, DateOnly from, DateOnly to, Guid? orderId, Guid? modelId, Guid? workerId, CancellationToken ct) => Results.Ok(ApiResponse.Success(await service.DailyReportAsync(from, to, orderId, modelId, workerId, ct)))).RequireAuthorization().RequirePermission("reports.financial.view").RequireRateLimiting(ApiRateLimitPolicies.NormalRead).WithTags("Production reports");
    }
    private static Guid RequireUser(ICurrentUserService user) => user.UserId ?? throw new UnauthorizedAccessException("User context is required.");

    private static async Task<RealDataIntakeUpload> ReadIntakeUploadAsync(HttpRequest request, CancellationToken ct)
    {
        if (!request.HasFormContentType) throw new ProductionConflictException("Controlled intake requires multipart form data.");
        var form = await request.ReadFormAsync(ct);
        var quantitiesJson = form["productionDayQuantities"].ToString();
        var quantities = JsonSerializer.Deserialize<ProductionDayQuantityInput[]>(quantitiesJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new ProductionConflictException("Final line quantities are required.");
        return new RealDataIntakeUpload(
            RequireFormValue(form, "factoryName"),
            RequireFormValue(form, "productionLineName"),
            RequireFormValue(form, "productName"),
            await ReadWorkbookAsync(form, "stagesWorkbook", ct),
            await ReadWorkbookAsync(form, "salaryWorkbook", ct),
            await ReadWorkbookAsync(form, "productionWorkbook", ct),
            quantities);
    }

    private static string RequireFormValue(IFormCollection form, string name)
    {
        var value = form[name].ToString().Trim();
        return string.IsNullOrWhiteSpace(value) ? throw new ProductionConflictException($"{name} is required.") : value;
    }

    private static async Task<IntakeWorkbookFile> ReadWorkbookAsync(IFormCollection form, string name, CancellationToken ct)
    {
        var file = form.Files.GetFile(name) ?? throw new ProductionConflictException($"{name} is required.");
        if (file.Length == 0 || file.Length > 20 * 1024 * 1024) throw new ProductionConflictException($"{name} must be between 1 byte and 20 MB.");
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        return new IntakeWorkbookFile(file.FileName, stream.ToArray());
    }
}

public sealed record MarkStageNotOperatedRequest(string Reason);
public sealed record ParticipantOverrideRequest(string Reason);
