using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Api.Authorization;
using ProductionLinePlanner.Api.Endpoints;
using ProductionLinePlanner.Api.Security;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Infrastructure.Importing;

namespace ProductionLinePlanner.Tests;

public sealed class ProductionCostRecordingHttpIntegrationTests
{
    [Fact]
    public async Task Daily_preview_validation_failure_returns_readable_problem_details()
    {
        await using var fixture = await ProductionHttpFixture.CreateAsync();

        var response = await fixture.SendAsync(HttpMethod.Post, "/api/production/daily-operations/preview", new
        {
            factoryId = fixture.FactoryId,
            productionLineId = fixture.LineId,
            productModelId = fixture.ModelId,
            productionDate = "2026-07-16",
            lineQuantity = 0m,
            clientRequestId = Guid.NewGuid(),
            stages = Array.Empty<object>()
        }, permissions: ["production.record"]);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(409, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Contains("كمية تشغيل الخط", problem.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Draft_preview_contract_is_post_only_and_returns_the_current_payload_calculation_for_new_and_reopened_drafts()
    {
        await using var fixture = await ProductionHttpFixture.CreateAsync(piecePrice: 0.38m);
        var recordPermissions = new[] { "production.record" };
        var allPermissions = new[] { "production.view", "production.record", "production.approve" };

        var wrongMethod = await fixture.SendAsync(HttpMethod.Get, "/api/production/records/preview", permissions: recordPermissions);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);

        var unauthorized = await fixture.SendAsync(HttpMethod.Post, "/api/production/records/preview", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var order = await fixture.SendAsync(HttpMethod.Post, "/api/production/orders", new { orderNumber = "PREVIEW-500", productModelId = fixture.ModelId, productionLineId = fixture.LineId, productionDate = "2026-07-13", plannedQuantity = 500m }, allPermissions);
        var orderId = (await DataAsync(order)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/orders/{orderId}/activate", new { }, allPermissions)).StatusCode);
        var payload = fixture.SingleWorkerDraftPayload(orderId);

        var preview = await fixture.SendAsync(HttpMethod.Post, "/api/production/records/preview", payload, recordPermissions);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var previewData = await DataAsync(preview);
        Assert.Equal(500m, previewData.GetProperty("acceptedQuantity").GetDecimal());
        Assert.Equal(190m, previewData.GetProperty("workers").EnumerateArray().Single().GetProperty("calculatedEarning").GetDecimal());
        Assert.Equal(190m, previewData.GetProperty("totalWorkerEarnings").GetDecimal());

        var saved = await fixture.SendAsync(HttpMethod.Post, "/api/production/records", payload, recordPermissions);
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        var savedData = await DataAsync(saved);
        Assert.Equal(190m, savedData.GetProperty("totalWorkerEarnings").GetDecimal());
        Assert.Equal(savedData.GetProperty("workers").EnumerateArray().Sum(worker => worker.GetProperty("calculatedEarning").GetDecimal()), savedData.GetProperty("totalWorkerEarnings").GetDecimal());

        var recordId = savedData.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Get, $"/api/production/records/{recordId}", permissions: new[] { "production.view" })).StatusCode);
        var reopenedPreview = await fixture.SendAsync(HttpMethod.Post, "/api/production/records/preview", fixture.SingleWorkerDraftPayload(orderId), recordPermissions);
        Assert.Equal(HttpStatusCode.OK, reopenedPreview.StatusCode);
        Assert.Equal(190m, (await DataAsync(reopenedPreview)).GetProperty("totalWorkerEarnings").GetDecimal());

        var approved = await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{recordId}/approve", new { concurrencyToken = savedData.GetProperty("concurrencyToken").GetGuid() }, allPermissions);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
    }

    [Fact]
    public async Task Product_readiness_reports_operational_and_financial_states_without_blocking_shared_percentage_workflow_test()
    {
        await using var fixture = await ProductionHttpFixture.CreateAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Get,
            $"/api/production/readiness?productModelId={fixture.ModelId}&productionLineId={fixture.LineId}&productionDate=2026-07-13",
            permissions: ["production.view"]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var readiness = await DataAsync(response);
        Assert.Equal(1, readiness.GetProperty("totalStages").GetInt32());
        Assert.Equal(1, readiness.GetProperty("readyStages").GetInt32());
        Assert.True(readiness.GetProperty("readyForWorkflowTest").GetBoolean());
        Assert.True(readiness.GetProperty("readyForProductionEntry").GetBoolean());
        Assert.False(readiness.GetProperty("readyForFinancialApproval").GetBoolean());
        Assert.Equal(1, readiness.GetProperty("stagesNeedingCompensationReview").GetInt32());
    }

    [Fact]
    public async Task Product_readiness_success_uses_the_standard_api_success_envelope()
    {
        await using var fixture = await ProductionHttpFixture.CreateAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Get,
            $"/api/production/readiness?productModelId={fixture.ModelId}&productionLineId={fixture.LineId}&productionDate=2026-07-13",
            permissions: ["production.view"]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1, document.RootElement.GetProperty("data").GetProperty("totalStages").GetInt32());
    }

    [Fact]
    public async Task Product_readiness_failure_uses_the_standard_api_failure_envelope()
    {
        await using var fixture = await ProductionHttpFixture.CreateAsync(readinessError: new Error("NotFound", "Readiness source not found."));

        var response = await fixture.SendAsync(
            HttpMethod.Get,
            $"/api/production/readiness?productModelId={fixture.ModelId}&productionLineId={fixture.LineId}&productionDate=2026-07-13",
            permissions: ["production.view"]);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("NotFound", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("Readiness source not found.", document.RootElement.GetProperty("error").GetProperty("message").GetString());
        Assert.False(document.RootElement.TryGetProperty("statusCode", out _));
    }

    [Fact]
    public async Task Production_endpoints_enforce_http_permission_boundaries()
    {
        await using var fixture = await ProductionHttpFixture.CreateAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.SendAsync(HttpMethod.Get, "/api/production/records")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.SendAsync(HttpMethod.Get, "/api/production/records", permissions: [])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Get, "/api/production/records", permissions: ["production.view"])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Get, "/api/production/reports/daily?from=2026-07-13&to=2026-07-13", permissions: ["production.view"])).StatusCode);

        var orderResponse = await fixture.SendAsync(HttpMethod.Post, "/api/production/orders", new { orderNumber = "AUTH-ORDER", productModelId = fixture.ModelId, productionLineId = fixture.LineId, productionDate = "2026-07-13", plannedQuantity = 500m }, ["production.record"]);
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.SendAsync(HttpMethod.Post, "/api/production/orders", new { orderNumber = "NO-RECORD", productModelId = fixture.ModelId, productionLineId = fixture.LineId, productionDate = "2026-07-13", plannedQuantity = 1m }, ["production.approve"])).StatusCode);

        var orderId = (await DataAsync(orderResponse)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/orders/{orderId}/activate", new { }, ["production.record"])).StatusCode);
        var draftResponse = await fixture.SendAsync(HttpMethod.Post, "/api/production/records", fixture.DraftPayload(orderId), ["production.record"]);
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        var draftData = await DataAsync(draftResponse); var draftId = draftData.GetProperty("id").GetGuid(); var draftToken = draftData.GetProperty("concurrencyToken").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{draftId}/approve", new { }, ["production.record"])).StatusCode);
        var approved = await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{draftId}/approve", new { concurrencyToken = draftToken }, ["production.approve"]);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var approvedToken = (await DataAsync(approved)).GetProperty("concurrencyToken").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{draftId}/cancel-production-approval", new { concurrencyToken = approvedToken, reason = "تصحيح اعتماد الإنتاج" }, ["production.record"])).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{draftId}/cancel-production-approval", new { concurrencyToken = approvedToken, reason = "" }, ["production.approve"])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{draftId}/cancel-production-approval", new { concurrencyToken = approvedToken, reason = "تصحيح اعتماد الإنتاج" }, ["production.approve"])).StatusCode);
    }

    [Fact]
    public async Task Production_record_permission_can_load_read_only_lookups_without_master_data_permissions()
    {
        await using var fixture = await ProductionHttpFixture.CreateAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.SendAsync(HttpMethod.Get, "/api/production/lookups/models", permissions: [])).StatusCode);
        var models = await fixture.SendAsync(HttpMethod.Get, "/api/production/lookups/models", permissions: ["production.record"]);
        Assert.Equal(HttpStatusCode.OK, models.StatusCode);
        Assert.Single((await DataAsync(models)).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Get, "/api/production/lookups/workers", permissions: ["production.record"])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Get, $"/api/production/lookups/models/{fixture.ModelId}/stages", permissions: ["production.record"])).StatusCode);

        var order = await fixture.SendAsync(HttpMethod.Post, "/api/production/orders", new { orderNumber = "LOOKUP-DRAFT", productModelId = fixture.ModelId, productionLineId = fixture.LineId, productionDate = "2026-07-13", plannedQuantity = 10m }, ["production.record"]);
        var orderId = (await DataAsync(order)).GetProperty("id").GetGuid();
        await fixture.SendAsync(HttpMethod.Post, $"/api/production/orders/{orderId}/activate", new { }, ["production.record"]);
        Assert.Equal(HttpStatusCode.Created, (await fixture.SendAsync(HttpMethod.Post, "/api/production/records", fixture.DraftPayload(orderId, 10m), ["production.record"])).StatusCode);
    }

    [Fact]
    public async Task Production_recording_http_end_to_end_keeps_500_pairs_and_excludes_cancelled_record()
    {
        await using var fixture = await ProductionHttpFixture.CreateAsync();
        var permissions = new[] { "production.view", "production.record", "production.approve" };
        var orderResponse = await fixture.SendAsync(HttpMethod.Post, "/api/production/orders", new { orderNumber = "E2E-500", productModelId = fixture.ModelId, productionLineId = fixture.LineId, productionDate = "2026-07-13", plannedQuantity = 500m }, permissions);
        var orderId = (await DataAsync(orderResponse)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/orders/{orderId}/activate", new { }, permissions)).StatusCode);
        var draft = await fixture.SendAsync(HttpMethod.Post, "/api/production/records", fixture.DraftPayload(orderId), permissions);
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var draftData = await DataAsync(draft); var recordId = draftData.GetProperty("id").GetGuid(); var draftToken = draftData.GetProperty("concurrencyToken").GetGuid();
        Assert.Equal(250m, draftData.GetProperty("totalWorkerEarnings").GetDecimal());
        Assert.Equal(draftData.GetProperty("workers").EnumerateArray().Sum(worker => worker.GetProperty("calculatedEarning").GetDecimal()), draftData.GetProperty("totalWorkerEarnings").GetDecimal());
        var approved = await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{recordId}/approve", new { concurrencyToken = draftToken }, permissions);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        var report = await fixture.SendAsync(HttpMethod.Get, "/api/production/reports/daily?from=2026-07-13&to=2026-07-13", permissions: permissions);
        var row = (await DataAsync(report)).EnumerateArray().Single();
        Assert.Equal(500m, row.GetProperty("producedQuantity").GetDecimal());
        Assert.Equal(250m, row.GetProperty("stageCost").GetDecimal());
        var workers = row.GetProperty("workers").EnumerateArray().OrderBy(x => x.GetProperty("workerName").GetString()).ToArray();
        Assert.All(workers, worker => { Assert.Equal(250m, worker.GetProperty("equivalentQuantity").GetDecimal()); Assert.Equal(125m, worker.GetProperty("calculatedEarning").GetDecimal()); });

        var approvedToken = (await DataAsync(approved)).GetProperty("concurrencyToken").GetGuid();
        var cancellation = await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{recordId}/cancel-production-approval", new { concurrencyToken = approvedToken, reason = "تصحيح اعتماد الإنتاج" }, permissions);
        Assert.Equal(HttpStatusCode.OK, cancellation.StatusCode);
        var cancelled = await DataAsync(cancellation);
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        Assert.Equal("تصحيح اعتماد الإنتاج", cancelled.GetProperty("approvalCancellationReason").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{recordId}/cancel-production-approval", new { concurrencyToken = cancelled.GetProperty("concurrencyToken").GetGuid(), reason = "تكرار" }, permissions)).StatusCode);
        var afterCancel = await fixture.SendAsync(HttpMethod.Get, "/api/production/reports/daily?from=2026-07-13&to=2026-07-13", permissions: permissions);
        Assert.Empty((await DataAsync(afterCancel)).EnumerateArray());
    }

    [Fact]
    public async Task Production_recording_ignores_a_client_supplied_total_and_returns_the_server_allocation_sum()
    {
        await using var fixture = await ProductionHttpFixture.CreateAsync();
        var permissions = new[] { "production.view", "production.record" };
        var order = await fixture.SendAsync(HttpMethod.Post, "/api/production/orders", new { orderNumber = "SERVER-TOTAL", productModelId = fixture.ModelId, productionLineId = fixture.LineId, productionDate = "2026-07-13", plannedQuantity = 500m }, permissions);
        var orderId = (await DataAsync(order)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/orders/{orderId}/activate", new { }, permissions)).StatusCode);

        var draft = await fixture.SendAsync(HttpMethod.Post, "/api/production/records", new
        {
            productionOrderId = orderId,
            productModelStageId = fixture.ModelStageId,
            productionDate = "2026-07-13",
            producedQuantity = 500m,
            acceptedQuantity = 500m,
            rejectedQuantity = 0m,
            clientRequestId = Guid.NewGuid(),
            totalWorkerEarnings = 0m,
            workers = new[] { new { workerId = fixture.WorkerAId, percentage = 50m }, new { workerId = fixture.WorkerBId, percentage = 50m } }
        }, permissions);

        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var saved = await DataAsync(draft);
        Assert.Equal(250m, saved.GetProperty("totalWorkerEarnings").GetDecimal());
        Assert.Equal(saved.GetProperty("workers").EnumerateArray().Sum(worker => worker.GetProperty("calculatedEarning").GetDecimal()), saved.GetProperty("totalWorkerEarnings").GetDecimal());
    }

    [Fact]
    public async Task Production_recording_http_returns_409_for_stale_update_approval_and_cancellation()
    {
        await using var fixture = await ProductionHttpFixture.CreateAsync();
        var permissions = new[] { "production.view", "production.record", "production.approve" };
        var order = await fixture.SendAsync(HttpMethod.Post, "/api/production/orders", new { orderNumber = "HTTP-CONCURRENCY", productModelId = fixture.ModelId, productionLineId = fixture.LineId, productionDate = "2026-07-13", plannedQuantity = 500m }, permissions);
        var orderId = (await DataAsync(order)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/orders/{orderId}/activate", new { }, permissions)).StatusCode);
        var draft = await fixture.SendAsync(HttpMethod.Post, "/api/production/records", fixture.DraftPayload(orderId), permissions);
        var draftData = await DataAsync(draft); var recordId = draftData.GetProperty("id").GetGuid(); var tokenA = draftData.GetProperty("concurrencyToken").GetGuid();
        var updated = await fixture.SendAsync(HttpMethod.Put, $"/api/production/records/{recordId}", fixture.UpdatePayload(tokenA), permissions);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var tokenB = (await DataAsync(updated)).GetProperty("concurrencyToken").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.SendAsync(HttpMethod.Put, $"/api/production/records/{recordId}", fixture.UpdatePayload(tokenA), permissions)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{recordId}/approve", new { concurrencyToken = tokenA }, permissions)).StatusCode);
        var approved = await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{recordId}/approve", new { concurrencyToken = tokenB }, permissions);
        var approvedToken = (await DataAsync(approved)).GetProperty("concurrencyToken").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{recordId}/approve", new { concurrencyToken = tokenB }, permissions)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{recordId}/approve", new { concurrencyToken = approvedToken }, permissions)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{recordId}/cancel-production-approval", new { concurrencyToken = tokenB, reason = "تصحيح اعتماد الإنتاج" }, permissions)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{recordId}/cancel-production-approval", new { concurrencyToken = approvedToken, reason = "تصحيح اعتماد الإنتاج" }, permissions)).StatusCode);
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").Clone();
    }

    private sealed class ProductionHttpFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly WebApplication _app;
        private readonly HttpClient _client;
        private readonly Guid _userId;
        private ProductionHttpFixture(SqliteConnection connection, WebApplication app, HttpClient client, Guid userId, Guid factoryId, Guid modelId, Guid lineId, Guid modelStageId, Guid workerAId, Guid workerBId)
        { _connection = connection; _app = app; _client = client; _userId = userId; FactoryId = factoryId; ModelId = modelId; LineId = lineId; ModelStageId = modelStageId; WorkerAId = workerAId; WorkerBId = workerBId; }
        public Guid FactoryId { get; } public Guid ModelId { get; } public Guid LineId { get; } public Guid ModelStageId { get; } public Guid WorkerAId { get; } public Guid WorkerBId { get; }

        public static async Task<ProductionHttpFixture> CreateAsync(decimal piecePrice = .50m, Error? readinessError = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:"); connection.CreateCollation("SQL_Latin1_General_CP1_CI_AS", (left, right) => string.Compare(left, right, StringComparison.OrdinalIgnoreCase)); await connection.OpenAsync();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "IntegrationTest" });
            builder.WebHost.UseTestServer();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("test", _ => { });
            builder.Services.AddAuthorization(options => options.AddPermissionPolicies());
            builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
            builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
            builder.Services.AddScoped<IPermissionService, HeaderPermissionService>();
            builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
            builder.Services.AddScoped<IAuditEngine, AuditEngine>();
            builder.Services.AddScoped<IAssignmentEngine, AssignmentEngine>();
            builder.Services.AddScoped<IAttendanceEngine, PresentAttendanceEngine>();
            builder.Services.AddScoped<IProductionCostRecordingService, ProductionCostRecordingService>();
            if (readinessError is null)
            {
                builder.Services.AddScoped<IProductionReadinessEngine, ProductionReadinessEngine>();
            }
            else
            {
                builder.Services.AddSingleton<IProductionReadinessEngine>(new FailingProductionReadinessEngine(readinessError));
            }
            builder.Services.AddScoped<IImportNormalizationService, ImportNormalizationService>();
            builder.Services.AddScoped<IRealDataIntakeService, RealDataIntakeService>();
            var app = builder.Build(); app.UseExceptionHandler(error => error.Run(context =>
            {
                var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                var status = exception is ProductionConflictException or DbUpdateConcurrencyException ? StatusCodes.Status409Conflict : StatusCodes.Status500InternalServerError;
                return Results.Problem(
                    title: status == StatusCodes.Status409Conflict ? "Conflict" : "Internal Server Error",
                    detail: exception?.Message ?? "An unexpected error occurred.",
                    statusCode: status).ExecuteAsync(context);
            })); app.UseAuthentication(); app.UseAuthorization(); app.MapProductionCostRecordingEndpoints(); await app.StartAsync();
            var userId = Guid.NewGuid(); Guid factoryId; Guid modelId; Guid lineId; Guid modelStageId; Guid workerAId; Guid workerBId;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); await db.Database.EnsureCreatedAsync();
                var factory = new Factory(Guid.NewGuid(), "Integration Factory", "INT"); var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Integration Line", 1); var main = new MainStage(Guid.NewGuid(), line.Id, "Main", 1); var sub = new SubStage(Guid.NewGuid(), main.Id, "Sew", "SEW", 1, 1);
                var model = new ProductModel(Guid.NewGuid(), "HTTP-500", "HTTP 500"); var stage = new ProductModelStage(Guid.NewGuid(), model.Id, sub.Id, 1, piecePrice, 17m, CompensationMode.SharedPercentage);
                var workerA = new Worker(Guid.NewGuid(), "A", "Worker A"); var workerB = new Worker(Guid.NewGuid(), "B", "Worker B"); var user = new AppUser(userId, "Integration User", "integration@example.test", "hash");
                db.AddRange(factory, line, main, sub, model, stage, workerA, workerB, user); await db.SaveChangesAsync();
                db.AddRange(new WorkerDefaultAssignment(Guid.NewGuid(), workerA.Id, sub.Id, userId, DateTime.UtcNow.AddMinutes(-1), "Integration assignment"), new WorkerDefaultAssignment(Guid.NewGuid(), workerB.Id, sub.Id, userId, DateTime.UtcNow.AddMinutes(-1), "Integration assignment"));
                await db.SaveChangesAsync(); factoryId = factory.Id; modelId = model.Id; lineId = line.Id; modelStageId = stage.Id; workerAId = workerA.Id; workerBId = workerB.Id;
            }
            return new ProductionHttpFixture(connection, app, app.GetTestClient(), userId, factoryId, modelId, lineId, modelStageId, workerAId, workerBId);
        }

        public object DraftPayload(Guid orderId, decimal quantity = 500m) => new { productionOrderId = orderId, productModelStageId = ModelStageId, productionDate = "2026-07-13", producedQuantity = quantity, acceptedQuantity = quantity, rejectedQuantity = 0m, clientRequestId = Guid.NewGuid(), workers = new[] { new { workerId = WorkerAId, percentage = 50m }, new { workerId = WorkerBId, percentage = 50m } } };
        public object SingleWorkerDraftPayload(Guid orderId) => new { productionOrderId = orderId, productModelStageId = ModelStageId, productionDate = "2026-07-13", producedQuantity = 500m, acceptedQuantity = 500m, rejectedQuantity = 0m, clientRequestId = Guid.NewGuid(), workers = new[] { new { workerId = WorkerAId, percentage = 100m } } };
        public object UpdatePayload(Guid concurrencyToken) => new { productionDate = "2026-07-13", producedQuantity = 500m, acceptedQuantity = 500m, rejectedQuantity = 0m, concurrencyToken, workers = new[] { new { workerId = WorkerAId, percentage = 50m }, new { workerId = WorkerBId, percentage = 50m } } };
        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, IReadOnlyCollection<string>? permissions = null)
        {
            var request = new HttpRequestMessage(method, path); if (body is not null) request.Content = JsonContent.Create(body); if (permissions is not null) { request.Headers.Add("X-Test-User", _userId.ToString()); request.Headers.Add("X-Test-Permissions", string.Join(',', permissions)); } return await _client.SendAsync(request);
        }
        public async ValueTask DisposeAsync() { await _app.DisposeAsync(); await _connection.DisposeAsync(); }
    }

    private sealed class HeaderPermissionService(IHttpContextAccessor accessor) : IPermissionService
    {
        public Task<IReadOnlyCollection<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<string>>((accessor.HttpContext?.Request.Headers["X-Test-Permissions"].ToString() ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        public Task<PermissionCatalogItemDto[]> GetCatalogAsync(CancellationToken cancellationToken = default) => Task.FromResult(Array.Empty<PermissionCatalogItemDto>());
    }

    private sealed class PresentAttendanceEngine : IAttendanceEngine
    {
        public Task<Result<AttendanceWorkerStateDto[]>> GetTodayAttendanceAsync(Guid? factoryId, Guid? lineId, DateTime? dateUtc, CancellationToken cancellationToken = default) => Task.FromResult(Result<AttendanceWorkerStateDto[]>.Success([]));
        public Task<Result<AttendanceRecordDto[]>> GetWorkerAttendanceAsync(Guid workerId, DateTime? fromDateUtc, DateTime? toDateUtc, CancellationToken cancellationToken = default) => Task.FromResult(Result<AttendanceRecordDto[]>.Success([]));
        public Task<Result<AttendanceSubStageAttendanceDto>> GetSubStageAttendanceAsync(Guid subStageId, DateTime? dateUtc, CancellationToken cancellationToken = default) => Task.FromResult(Result<AttendanceSubStageAttendanceDto>.Success(new AttendanceSubStageAttendanceDto()));
        public Task<Result<AttendanceSyncResultDto>> SyncTodayAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result<AttendanceSyncResultDto>.Success(new AttendanceSyncResultDto()));
        public Task<Result<AttendanceSyncResultDto>> SyncForProductionDateAsync(DateOnly productionDate, CancellationToken cancellationToken = default) => Task.FromResult(Result<AttendanceSyncResultDto>.Success(new AttendanceSyncResultDto()));
        public Task<Result<Dictionary<Guid, AttendanceStatusRecord>>> GetLatestAttendanceStatusByWorkerAsync(IEnumerable<Guid> workerIds, DateTime? asOfUtc = null, CancellationToken cancellationToken = default) => Task.FromResult(Result<Dictionary<Guid, AttendanceStatusRecord>>.Success(workerIds.Distinct().ToDictionary(id => id, id => new AttendanceStatusRecord(id, AttendanceStatus.Present, asOfUtc ?? DateTime.UtcNow, "test"))));
    }

    private sealed class FailingProductionReadinessEngine(Error error) : IProductionReadinessEngine
    {
        public Task<Result<ProductProductionReadinessDto>> GetProductReadinessAsync(
            Guid productModelId,
            Guid productionLineId,
            DateOnly productionDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ProductProductionReadinessDto>.Failure(error));
    }

    private sealed class HeaderAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var raw = Request.Headers["X-Test-User"].ToString(); if (!Guid.TryParse(raw, out var userId)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Name, "integration-user")], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
