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

namespace ProductionLinePlanner.Tests;

public sealed class ProductionCostRecordingHttpIntegrationTests
{
    [Fact]
    public async Task Production_endpoints_enforce_http_permission_boundaries()
    {
        await using var fixture = await ProductionHttpFixture.CreateAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.SendAsync(HttpMethod.Get, "/api/production/records")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.SendAsync(HttpMethod.Get, "/api/production/records", permissions: [])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Get, "/api/production/records", permissions: ["production.view"])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Get, "/api/production/reports/daily?from=2026-07-13&to=2026-07-13", permissions: ["production.view"])).StatusCode);

        var orderResponse = await fixture.SendAsync(HttpMethod.Post, "/api/production/orders", new { orderNumber = "AUTH-ORDER", productModelId = fixture.ModelId, productionDate = "2026-07-13", plannedQuantity = 500m }, ["production.record"]);
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.SendAsync(HttpMethod.Post, "/api/production/orders", new { orderNumber = "NO-RECORD", productModelId = fixture.ModelId, productionDate = "2026-07-13", plannedQuantity = 1m }, ["production.approve"])).StatusCode);

        var orderId = (await DataAsync(orderResponse)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/orders/{orderId}/activate", new { }, ["production.record"])).StatusCode);
        var draftResponse = await fixture.SendAsync(HttpMethod.Post, "/api/production/records", fixture.DraftPayload(orderId), ["production.record"]);
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        var draftData = await DataAsync(draftResponse); var draftId = draftData.GetProperty("id").GetGuid(); var draftToken = draftData.GetProperty("concurrencyToken").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{draftId}/approve", new { }, ["production.record"])).StatusCode);
        var approved = await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{draftId}/approve", new { concurrencyToken = draftToken }, ["production.approve"]);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var approvedToken = (await DataAsync(approved)).GetProperty("concurrencyToken").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{draftId}/cancel", new { concurrencyToken = approvedToken }, ["production.approve"])).StatusCode);
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

        var order = await fixture.SendAsync(HttpMethod.Post, "/api/production/orders", new { orderNumber = "LOOKUP-DRAFT", productModelId = fixture.ModelId, productionDate = "2026-07-13", plannedQuantity = 10m }, ["production.record"]);
        var orderId = (await DataAsync(order)).GetProperty("id").GetGuid();
        await fixture.SendAsync(HttpMethod.Post, $"/api/production/orders/{orderId}/activate", new { }, ["production.record"]);
        Assert.Equal(HttpStatusCode.Created, (await fixture.SendAsync(HttpMethod.Post, "/api/production/records", fixture.DraftPayload(orderId, 10m), ["production.record"])).StatusCode);
    }

    [Fact]
    public async Task Production_recording_http_end_to_end_keeps_500_pairs_and_excludes_cancelled_record()
    {
        await using var fixture = await ProductionHttpFixture.CreateAsync();
        var permissions = new[] { "production.view", "production.record", "production.approve" };
        var orderResponse = await fixture.SendAsync(HttpMethod.Post, "/api/production/orders", new { orderNumber = "E2E-500", productModelId = fixture.ModelId, productionDate = "2026-07-13", plannedQuantity = 500m }, permissions);
        var orderId = (await DataAsync(orderResponse)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/orders/{orderId}/activate", new { }, permissions)).StatusCode);
        var draft = await fixture.SendAsync(HttpMethod.Post, "/api/production/records", fixture.DraftPayload(orderId), permissions);
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var draftData = await DataAsync(draft); var recordId = draftData.GetProperty("id").GetGuid(); var draftToken = draftData.GetProperty("concurrencyToken").GetGuid();
        var approved = await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{recordId}/approve", new { concurrencyToken = draftToken }, permissions);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        var report = await fixture.SendAsync(HttpMethod.Get, "/api/production/reports/daily?from=2026-07-13&to=2026-07-13", permissions: permissions);
        var row = (await DataAsync(report)).EnumerateArray().Single();
        Assert.Equal(500m, row.GetProperty("producedQuantity").GetDecimal());
        Assert.Equal(250m, row.GetProperty("stageCost").GetDecimal());
        var workers = row.GetProperty("workers").EnumerateArray().OrderBy(x => x.GetProperty("workerName").GetString()).ToArray();
        Assert.All(workers, worker => { Assert.Equal(250m, worker.GetProperty("equivalentQuantity").GetDecimal()); Assert.Equal(125m, worker.GetProperty("calculatedEarning").GetDecimal()); });

        var approvedToken = (await DataAsync(approved)).GetProperty("concurrencyToken").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{recordId}/cancel", new { concurrencyToken = approvedToken }, permissions)).StatusCode);
        var afterCancel = await fixture.SendAsync(HttpMethod.Get, "/api/production/reports/daily?from=2026-07-13&to=2026-07-13", permissions: permissions);
        Assert.Empty((await DataAsync(afterCancel)).EnumerateArray());
    }

    [Fact]
    public async Task Production_recording_http_returns_409_for_stale_update_approval_and_cancellation()
    {
        await using var fixture = await ProductionHttpFixture.CreateAsync();
        var permissions = new[] { "production.view", "production.record", "production.approve" };
        var order = await fixture.SendAsync(HttpMethod.Post, "/api/production/orders", new { orderNumber = "HTTP-CONCURRENCY", productModelId = fixture.ModelId, productionDate = "2026-07-13", plannedQuantity = 500m }, permissions);
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
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{recordId}/cancel", new { concurrencyToken = tokenB }, permissions)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Post, $"/api/production/records/{recordId}/cancel", new { concurrencyToken = approvedToken }, permissions)).StatusCode);
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
        private ProductionHttpFixture(SqliteConnection connection, WebApplication app, HttpClient client, Guid userId, Guid modelId, Guid modelStageId, Guid workerAId, Guid workerBId)
        { _connection = connection; _app = app; _client = client; _userId = userId; ModelId = modelId; ModelStageId = modelStageId; WorkerAId = workerAId; WorkerBId = workerBId; }
        public Guid ModelId { get; } public Guid ModelStageId { get; } public Guid WorkerAId { get; } public Guid WorkerBId { get; }

        public static async Task<ProductionHttpFixture> CreateAsync()
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
            builder.Services.AddScoped<IProductionCostRecordingService, ProductionCostRecordingService>();
            var app = builder.Build(); app.UseExceptionHandler(error => error.Run(context =>
            {
                var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                context.Response.StatusCode = exception is ProductionConflictException or DbUpdateConcurrencyException ? StatusCodes.Status409Conflict : StatusCodes.Status500InternalServerError;
                return Task.CompletedTask;
            })); app.UseAuthentication(); app.UseAuthorization(); app.MapProductionCostRecordingEndpoints(); await app.StartAsync();
            var userId = Guid.NewGuid(); Guid modelId; Guid modelStageId; Guid workerAId; Guid workerBId;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); await db.Database.EnsureCreatedAsync();
                var factory = new Factory(Guid.NewGuid(), "Integration Factory", "INT"); var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Integration Line", 1); var main = new MainStage(Guid.NewGuid(), line.Id, "Main", 1); var sub = new SubStage(Guid.NewGuid(), main.Id, "Sew", "SEW", 1, 1);
                var model = new ProductModel(Guid.NewGuid(), "HTTP-500", "HTTP 500"); var stage = new ProductModelStage(Guid.NewGuid(), model.Id, sub.Id, 1, .50m, 17m, CompensationMode.SharedPercentage);
                var workerA = new Worker(Guid.NewGuid(), "A", "Worker A"); var workerB = new Worker(Guid.NewGuid(), "B", "Worker B"); var user = new AppUser(userId, "Integration User", "integration@example.test", "hash");
                db.AddRange(factory, line, main, sub, model, stage, workerA, workerB, user); await db.SaveChangesAsync(); modelId = model.Id; modelStageId = stage.Id; workerAId = workerA.Id; workerBId = workerB.Id;
            }
            return new ProductionHttpFixture(connection, app, app.GetTestClient(), userId, modelId, modelStageId, workerAId, workerBId);
        }

        public object DraftPayload(Guid orderId, decimal quantity = 500m) => new { productionOrderId = orderId, productModelStageId = ModelStageId, productionDate = "2026-07-13", producedQuantity = quantity, acceptedQuantity = quantity, rejectedQuantity = 0m, clientRequestId = Guid.NewGuid(), workers = new[] { new { workerId = WorkerAId, percentage = 50m }, new { workerId = WorkerBId, percentage = 50m } } };
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
