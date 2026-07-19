using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
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
using ProductionLinePlanner.Application.Services;

namespace ProductionLinePlanner.Tests;

public sealed class WorkerPhotoHttpIntegrationTests
{
    [Fact]
    public async Task Photo_routes_enforce_view_for_download_and_manage_for_writes()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.SendGetAsync(null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.SendGetAsync(["workers.manage"])).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.SendUploadAsync(["workers.view"])).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.SendDeleteAsync(["workers.view"])).StatusCode);
        Assert.Equal(0, fixture.Service.UploadCalls);
        Assert.Equal(0, fixture.Service.DeleteCalls);

        Assert.Equal(HttpStatusCode.Created, (await fixture.SendUploadAsync(["workers.manage"])).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await fixture.SendDeleteAsync(["workers.manage"])).StatusCode);
        Assert.Equal(1, fixture.Service.UploadCalls);
        Assert.Equal(1, fixture.Service.DeleteCalls);
    }

    [Fact]
    public async Task Versioned_download_requires_private_authorized_revalidation_and_supports_etag()
    {
        await using var fixture = await Fixture.CreateAsync();

        var first = await fixture.SendGetAsync(["workers.view"], includeVersion: true);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("image/jpeg", first.Content.Headers.ContentType?.MediaType);
        Assert.True(first.Headers.CacheControl?.Private);
        Assert.True(first.Headers.CacheControl?.NoCache);
        Assert.True(first.Headers.CacheControl?.MustRevalidate);
        Assert.Equal($"\"{fixture.Service.Version}\"", first.Headers.ETag?.ToString());
        Assert.Contains("Authorization", first.Headers.Vary);

        var revalidated = await fixture.SendGetAsync(
            ["workers.view"],
            includeVersion: true,
            ifNoneMatch: $"\"{fixture.Service.Version}\"");
        Assert.Equal(HttpStatusCode.NotModified, revalidated.StatusCode);
        Assert.Equal(2, fixture.Service.DownloadCalls);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly WebApplication app;
        private readonly HttpClient client;
        private readonly Guid userId = Guid.NewGuid();

        private Fixture(WebApplication app, HttpClient client, WorkerPhotoServiceStub service)
        {
            this.app = app;
            this.client = client;
            Service = service;
        }

        public Guid WorkerId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public WorkerPhotoServiceStub Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var service = new WorkerPhotoServiceStub();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "IntegrationTest" });
            builder.WebHost.UseTestServer();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddAuthentication("test")
                .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("test", _ => { });
            builder.Services.AddAuthorization(options => options.AddPermissionPolicies());
            builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
            builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
            builder.Services.AddScoped<IPermissionService, HeaderPermissionService>();
            builder.Services.AddSingleton<IWorkerPhotoService>(service);
            builder.Services.AddRateLimiter(options =>
            {
                options.AddFixedWindowLimiter(ApiRateLimitPolicies.WorkerPhotoRead, limiter => ConfigureLimiter(limiter));
                options.AddFixedWindowLimiter(ApiRateLimitPolicies.WorkerPhotoWrite, limiter => ConfigureLimiter(limiter));
            });

            var app = builder.Build();
            app.UseAuthentication();
            app.UseRateLimiter();
            app.UseAuthorization();
            app.MapGroup("/api/workers").RequireAuthorization().MapWorkerPhotoEndpoints();
            await app.StartAsync();
            return new Fixture(app, app.GetTestClient(), service);
        }

        public Task<HttpResponseMessage> SendGetAsync(
            IReadOnlyCollection<string>? permissions,
            bool includeVersion = false,
            string? ifNoneMatch = null)
        {
            var path = $"/api/workers/{WorkerId:D}/photo";
            if (includeVersion) path += $"?v={Service.Version}";
            var request = CreateRequest(HttpMethod.Get, path, permissions);
            if (ifNoneMatch is not null) request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
            return client.SendAsync(request);
        }

        public Task<HttpResponseMessage> SendUploadAsync(IReadOnlyCollection<string> permissions)
        {
            var request = CreateRequest(HttpMethod.Put, $"/api/workers/{WorkerId:D}/photo", permissions);
            var multipart = new MultipartFormDataContent();
            var photo = new ByteArrayContent(WorkerPhotoTestData.CreateJpeg());
            photo.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            multipart.Add(photo, "photo", "worker.jpg");
            request.Content = multipart;
            return client.SendAsync(request);
        }

        public Task<HttpResponseMessage> SendDeleteAsync(IReadOnlyCollection<string> permissions) =>
            client.SendAsync(CreateRequest(HttpMethod.Delete, $"/api/workers/{WorkerId:D}/photo", permissions));

        private HttpRequestMessage CreateRequest(
            HttpMethod method,
            string path,
            IReadOnlyCollection<string>? permissions)
        {
            var request = new HttpRequestMessage(method, path);
            if (permissions is not null)
            {
                request.Headers.Add("X-Test-User", userId.ToString());
                request.Headers.Add("X-Test-Permissions", string.Join(',', permissions));
            }
            return request;
        }

        private static void ConfigureLimiter(FixedWindowRateLimiterOptions limiter)
        {
            limiter.PermitLimit = 100;
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.QueueLimit = 0;
        }

        public async ValueTask DisposeAsync()
        {
            await app.DisposeAsync();
            client.Dispose();
        }
    }

    public sealed class WorkerPhotoServiceStub : IWorkerPhotoService
    {
        public string Version { get; } = new string('a', 64);
        public int UploadCalls { get; private set; }
        public int DownloadCalls { get; private set; }
        public int DeleteCalls { get; private set; }

        public Task<Result<WorkerPhotoChangeResult>> UploadAsync(
            Guid workerId,
            Stream content,
            long declaredLength,
            string? declaredContentType,
            Guid actorUserId,
            string? requestMeta = null,
            CancellationToken cancellationToken = default)
        {
            UploadCalls++;
            var metadata = new WorkerPhotoMetadata(
                workerId,
                $"/api/workers/{workerId:D}/photo?v={Version}",
                Version,
                "image/jpeg",
                declaredLength);
            return Task.FromResult(Result<WorkerPhotoChangeResult>.Success(new WorkerPhotoChangeResult(metadata, true, false, false)));
        }

        public Task<Result<WorkerPhotoDownload>> DownloadAsync(
            Guid workerId,
            string? requestedVersion = null,
            CancellationToken cancellationToken = default)
        {
            DownloadCalls++;
            return Task.FromResult(Result<WorkerPhotoDownload>.Success(new WorkerPhotoDownload(
                WorkerPhotoTestData.CreateJpeg(),
                "image/jpeg",
                Version)));
        }

        public Task<Result> DeleteAsync(
            Guid workerId,
            Guid actorUserId,
            string? requestMeta = null,
            CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            return Task.FromResult(Result.Success());
        }
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
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Name, "worker-photo-test")],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
