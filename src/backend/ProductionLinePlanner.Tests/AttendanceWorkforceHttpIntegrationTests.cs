using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
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
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Application.Services;

namespace ProductionLinePlanner.Tests;

public sealed class AttendanceWorkforceHttpIntegrationTests
{
    [Fact]
    public async Task List_and_detail_require_both_attendance_and_assignment_permissions()
    {
        await using var fixture = await AttendanceFixture.CreateAsync();
        foreach (var path in new[] { "/api/attendance/workforce", $"/api/attendance/workforce/workers/{fixture.WorkerId}/details" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await fixture.GetAsync(path, ["attendance.view"])).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await fixture.GetAsync(path, ["assignments.view"])).StatusCode);
        }
    }

    [Fact]
    public async Task Authorized_list_and_detail_return_the_public_contract()
    {
        await using var fixture = await AttendanceFixture.CreateAsync();
        var permissions = new[] { "attendance.view", "assignments.view" };

        var list = await fixture.GetAsync("/api/attendance/workforce?productionDate=2026-07-19", permissions);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.True(listJson.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Array, listJson.RootElement.GetProperty("data").GetProperty("items").ValueKind);
        Assert.Equal(1, listJson.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32());
        var row = listJson.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Null, row.GetProperty("firstCheckInUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("lastCheckOutUtc").ValueKind);

        var detail = await fixture.GetAsync($"/api/attendance/workforce/workers/{fixture.WorkerId}/details?productionDate=2026-07-19", permissions);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal(fixture.WorkerId, detailJson.RootElement.GetProperty("data").GetProperty("workerId").GetGuid());
        Assert.Equal(JsonValueKind.Array, detailJson.RootElement.GetProperty("data").GetProperty("attendanceRecords").ValueKind);
    }

    [Fact]
    public async Task Worker_profile_summary_requires_only_attendance_view_and_returns_lightweight_attendance_data()
    {
        await using var fixture = await AttendanceFixture.CreateAsync();
        var path = $"/api/attendance/workforce/workers/{fixture.WorkerId}/summary?productionDate=2026-07-19";

        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.GetAsync(path, ["workers.view"])).StatusCode);
        var response = await fixture.GetAsync(path, ["attendance.view"]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal("Present", data.GetProperty("todayStatus").GetString());
        Assert.Equal(fixture.WorkerId, data.GetProperty("workerId").GetGuid());
        Assert.False(data.TryGetProperty("assignments", out _));
    }

    [Fact]
    public async Task Worker_attendance_history_requires_attendance_view_and_forwards_server_pagination()
    {
        await using var fixture = await AttendanceFixture.CreateAsync();
        var path = $"/api/workers/{fixture.WorkerId}/attendance-records?fromDate=2026-07-01&toDate=2026-07-29&page=2&pageSize=10";

        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.GetAsync(path, ["workers.view"])).StatusCode);
        var response = await fixture.GetAsync(path, ["attendance.view"]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(fixture.WorkerId, json.RootElement.GetProperty("data").GetProperty("workerId").GetGuid());
        Assert.Equal(2, json.RootElement.GetProperty("data").GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task Manual_sync_requires_attendance_sync_and_invokes_the_existing_engine_when_authorized()
    {
        await using var fixture = await AttendanceFixture.CreateAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.PostAsync("/api/attendance/sync/production-date/2026-07-19", ["attendance.view"])).StatusCode);

        var authorized = await fixture.PostAsync("/api/attendance/sync/production-date/2026-07-19", ["attendance.sync"]);

        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        Assert.Equal(new DateOnly(2026, 7, 19), fixture.AttendanceEngine.LastSyncedDate);
    }

    private sealed class AttendanceFixture : IAsyncDisposable
    {
        private readonly WebApplication app;
        private readonly HttpClient client;
        private readonly Guid userId = Guid.NewGuid();
        private AttendanceFixture(WebApplication app, HttpClient client, Guid workerId, AttendanceEngineStub attendanceEngine)
        { this.app = app; this.client = client; WorkerId = workerId; AttendanceEngine = attendanceEngine; }
        public Guid WorkerId { get; }
        public AttendanceEngineStub AttendanceEngine { get; }

        public static async Task<AttendanceFixture> CreateAsync()
        {
            var workerId = Guid.NewGuid();
            var attendanceEngine = new AttendanceEngineStub();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "IntegrationTest" });
            builder.WebHost.UseTestServer();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("test", _ => { });
            builder.Services.AddAuthorization(options => options.AddPermissionPolicies());
            builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
            builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
            builder.Services.AddScoped<IPermissionService, HeaderPermissionService>();
            builder.Services.AddSingleton<IAttendanceEngine>(attendanceEngine);
            builder.Services.AddSingleton<IAttendanceWorkforceEngine>(new WorkforceEngineStub(workerId));
            builder.Services.AddSingleton<ICairoTimeZoneProvider>(new CairoTimeZoneProviderStub());
            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGroup("/api/attendance").RequireAuthorization().MapAttendanceWorkforceEndpoints();
            app.MapGroup("/api/workers").RequireAuthorization().MapWorkerAttendanceHistoryEndpoints();
            await app.StartAsync();
            return new AttendanceFixture(app, app.GetTestClient(), workerId, attendanceEngine);
        }

        public Task<HttpResponseMessage> GetAsync(string path, IReadOnlyCollection<string> permissions) => SendAsync(HttpMethod.Get, path, permissions);
        public Task<HttpResponseMessage> PostAsync(string path, IReadOnlyCollection<string> permissions) => SendAsync(HttpMethod.Post, path, permissions);
        private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, IReadOnlyCollection<string> permissions)
        {
            var request = new HttpRequestMessage(method, path);
            request.Headers.Add("X-Test-User", userId.ToString());
            request.Headers.Add("X-Test-Permissions", string.Join(',', permissions));
            return client.SendAsync(request);
        }
        public async ValueTask DisposeAsync() { await app.DisposeAsync(); client.Dispose(); }
    }

    private sealed class WorkforceEngineStub(Guid workerId) : IAttendanceWorkforceEngine
    {
        public Task<Result<AttendanceWorkforcePageDto>> GetPageAsync(AttendanceWorkforceQuery query, CancellationToken cancellationToken = default)
        {
            var summary = new AttendanceWorkforceSummaryDto(1, 1, 0, 0, 0, 0, 0, 0, true, "current-page");
            var row = new AttendanceWorkforceRowDto(workerId, "W-1", "Worker", null, null, false, "Present", null, null, true, false, [], false, false, false);
            return Task.FromResult(Result<AttendanceWorkforcePageDto>.Success(new AttendanceWorkforcePageDto(query.ProductionDate, [row], summary, 1, 25, 1, 1)));
        }
        public Task<Result<AttendanceWorkforceDetailDto>> GetWorkerDetailAsync(Guid id, DateOnly productionDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<AttendanceWorkforceDetailDto>.Success(new AttendanceWorkforceDetailDto(id, productionDate, [], [])));
        public Task<Result<WorkerAttendanceProfileSummaryDto>> GetWorkerProfileSummaryAsync(Guid id, DateOnly productionDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<WorkerAttendanceProfileSummaryDto>.Success(new WorkerAttendanceProfileSummaryDto(id, productionDate, "Present", true, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow)));
        public Task<Result<WorkerAttendanceHistoryPageDto>> GetWorkerAttendanceHistoryAsync(Guid id, WorkerAttendanceHistoryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<WorkerAttendanceHistoryPageDto>.Success(new WorkerAttendanceHistoryPageDto(id, query.FromDate, query.ToDate, [], query.Page, query.PageSize, 0, 1)));
    }

    public sealed class AttendanceEngineStub : IAttendanceEngine
    {
        public DateOnly? LastSyncedDate { get; private set; }
        public Task<Result<AttendanceSyncResultDto>> SyncForProductionDateAsync(DateOnly productionDate, CancellationToken cancellationToken = default)
        { LastSyncedDate = productionDate; return Task.FromResult(Result<AttendanceSyncResultDto>.Success(new AttendanceSyncResultDto())); }
        public Task<Result<AttendanceSyncResultDto>> SyncTodayAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AttendanceWorkerStateDto[]>> GetTodayAttendanceAsync(Guid? factoryId, Guid? lineId, DateTime? dateUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AttendanceRecordDto[]>> GetWorkerAttendanceAsync(Guid workerId, DateTime? fromDateUtc, DateTime? toDateUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AttendanceSubStageAttendanceDto>> GetSubStageAttendanceAsync(Guid subStageId, DateTime? dateUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<Dictionary<Guid, AttendanceStatusRecord>>> GetLatestAttendanceStatusByWorkerAsync(IEnumerable<Guid> workerIds, DateTime? asOfUtc = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<Dictionary<Guid, AttendancePresenceWindowDto>>> GetPresenceWindowsByWorkerAsync(IEnumerable<Guid> workerIds, DateOnly productionDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class HeaderPermissionService(IHttpContextAccessor accessor) : IPermissionService
    {
        public Task<IReadOnlyCollection<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<string>>((accessor.HttpContext?.Request.Headers["X-Test-Permissions"].ToString() ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        public Task<PermissionCatalogItemDto[]> GetCatalogAsync(CancellationToken cancellationToken = default) => Task.FromResult(Array.Empty<PermissionCatalogItemDto>());
    }

    private sealed class CairoTimeZoneProviderStub : ICairoTimeZoneProvider
    {
        public TimeZoneInfo TimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone("Test Cairo", TimeSpan.FromHours(3), "Test Cairo", "Test Cairo");
    }

    private sealed class HeaderAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var raw = Request.Headers["X-Test-User"].ToString();
            if (!Guid.TryParse(raw, out var userId)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Name, "integration-user")], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
