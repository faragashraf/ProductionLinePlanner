using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
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
using ProductionLinePlanner.Application.Services;

namespace ProductionLinePlanner.Tests;

public sealed class WorkerSyncHttpIntegrationTests
{
    [Fact]
    public async Task Preview_requires_workers_manage_and_invokes_only_preview()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.SendAsync(HttpMethod.Get, "/api/workers/sync/preview", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.SendAsync(HttpMethod.Get, "/api/workers/sync/preview", ["workers.view"])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAsync(HttpMethod.Get, "/api/workers/sync/preview", ["workers.manage"])).StatusCode);
        Assert.Equal(1, fixture.SyncService.PreviewCalls);
        Assert.Equal(0, fixture.SyncService.ApplyCalls);
    }

    [Fact]
    public async Task Apply_endpoint_is_not_mapped_in_foundation()
    {
        await using var fixture = await Fixture.CreateAsync();

        var response = await fixture.SendAsync(HttpMethod.Post, "/api/workers/sync", ["workers.manage"]);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, fixture.SyncService.ApplyCalls);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly WebApplication app;
        private readonly HttpClient client;
        private readonly Guid userId = Guid.NewGuid();

        private Fixture(WebApplication app, HttpClient client, WorkerSyncServiceStub syncService)
        {
            this.app = app;
            this.client = client;
            SyncService = syncService;
        }

        public WorkerSyncServiceStub SyncService { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var syncService = new WorkerSyncServiceStub();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "IntegrationTest" });
            builder.WebHost.UseTestServer();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("test", _ => { });
            builder.Services.AddAuthorization(options => options.AddPermissionPolicies());
            builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
            builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
            builder.Services.AddScoped<IPermissionService, HeaderPermissionService>();
            builder.Services.AddSingleton<IWorkerInitialSyncService>(syncService);
            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGroup("/api/workers").RequireAuthorization().MapWorkerSyncEndpoints();
            await app.StartAsync();
            return new Fixture(app, app.GetTestClient(), syncService);
        }

        public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, IReadOnlyCollection<string>? permissions)
        {
            var request = new HttpRequestMessage(method, path);
            if (permissions is not null)
            {
                request.Headers.Add("X-Test-User", userId.ToString());
                request.Headers.Add("X-Test-Permissions", string.Join(',', permissions));
            }
            return client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            await app.DisposeAsync();
            client.Dispose();
        }
    }

    public sealed class WorkerSyncServiceStub : IWorkerInitialSyncService
    {
        public int PreviewCalls { get; private set; }
        public int ApplyCalls { get; private set; }

        public Task<Result<WorkerActiveServiceSyncPreviewDto>> PreviewActiveServiceSyncAsync(CancellationToken cancellationToken = default)
        {
            PreviewCalls++;
            return Task.FromResult(Result<WorkerActiveServiceSyncPreviewDto>.Success(new WorkerActiveServiceSyncPreviewDto()));
        }

        public Task<Result<WorkerInitialSyncResultDto>> SyncWorkersAsync(Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            throw new InvalidOperationException("Apply must not be invoked from the preview endpoint.");
        }

        public Task<Result<WorkerInitialSyncResultDto>> SyncWorkersForAttendanceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<WorkerInitialSyncResultDto>.Success(new WorkerInitialSyncResultDto()));
    }

    private sealed class HeaderPermissionService(IHttpContextAccessor accessor) : IPermissionService
    {
        public Task<IReadOnlyCollection<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<string>>((accessor.HttpContext?.Request.Headers["X-Test-Permissions"].ToString() ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        public Task<PermissionCatalogItemDto[]> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Array.Empty<PermissionCatalogItemDto>());
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var raw = Request.Headers["X-Test-User"].ToString();
            if (!Guid.TryParse(raw, out var userId)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Name, "worker-sync-test")],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
