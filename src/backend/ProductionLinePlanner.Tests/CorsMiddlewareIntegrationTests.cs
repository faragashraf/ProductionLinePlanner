using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ProductionLinePlanner.Tests;

public sealed class CorsMiddlewareIntegrationTests
{
    private const string AllowedOrigin = "http://tablet.local.test:4200";
    private const string LoopbackAllowedOrigin = "http://127.0.0.1:4200";
    private const string LanAllowedOrigin = "http://192.168.1.99";
    private const string DisallowedOrigin = "http://tablet.local.test:4300";
    private const string ManufacturingCorrelationHeader = "X-Manufacturing-Realtime-Correlation-Id";

    [Fact]
    public async Task Notification_hub_negotiate_preflight_allows_the_signalr_header_for_an_exact_configured_origin()
    {
        await using var fixture = await CorsFixture.CreateAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Options,
            "/hubs/notifications/negotiate?negotiateVersion=1",
            LanAllowedOrigin,
            accessControlRequestMethod: "POST",
            accessControlRequestHeaders: "content-type,x-signalr-user-agent,authorization");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(LanAllowedOrigin, CorsOrigin(response));
        Assert.Contains("POST", string.Join(',', response.Headers.GetValues("Access-Control-Allow-Methods")));
        var allowedHeaders = string.Join(',', response.Headers.GetValues("Access-Control-Allow-Headers"));
        Assert.Contains("content-type", allowedHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("x-signalr-user-agent", allowedHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("authorization", allowedHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
    }

    [Fact]
    public async Task Notification_hub_negotiate_preflight_does_not_grant_an_unconfigured_origin()
    {
        await using var fixture = await CorsFixture.CreateAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Options,
            "/hubs/notifications/negotiate?negotiateVersion=1",
            "http://evil.example",
            accessControlRequestMethod: "POST",
            accessControlRequestHeaders: "content-type,x-signalr-user-agent,authorization");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Allowed_origin_preflight_for_preview_returns_cors_headers()
    {
        await using var fixture = await CorsFixture.CreateAsync();

        var response = await fixture.SendAsync(HttpMethod.Options, "/api/production/records/preview", AllowedOrigin, accessControlRequestMethod: "POST");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(AllowedOrigin, CorsOrigin(response));
        Assert.Contains("POST", string.Join(',', response.Headers.GetValues("Access-Control-Allow-Methods")));
    }

    [Fact]
    public async Task Model_stage_patch_preflight_allows_the_manufacturing_correlation_header_for_the_configured_lan_origin()
    {
        await using var fixture = await CorsFixture.CreateAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Options,
            "/api/product-models/11111111-1111-1111-1111-111111111111/stages/22222222-2222-2222-2222-222222222222",
            LanAllowedOrigin,
            accessControlRequestMethod: "PATCH",
            accessControlRequestHeaders: "authorization,content-type,x-manufacturing-realtime-correlation-id");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(LanAllowedOrigin, CorsOrigin(response));
        Assert.Contains("PATCH", string.Join(',', response.Headers.GetValues("Access-Control-Allow-Methods")));
        var allowedHeaders = string.Join(',', response.Headers.GetValues("Access-Control-Allow-Headers"));
        Assert.Contains(ManufacturingCorrelationHeader, allowedHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("*", CorsOrigin(response));
    }

    [Fact]
    public async Task Model_stage_patch_preflight_does_not_grant_an_unconfigured_origin()
    {
        await using var fixture = await CorsFixture.CreateAsync();

        var response = await fixture.SendAsync(
            HttpMethod.Options,
            "/api/product-models/11111111-1111-1111-1111-111111111111/stages/22222222-2222-2222-2222-222222222222",
            DisallowedOrigin,
            accessControlRequestMethod: "PATCH",
            accessControlRequestHeaders: "authorization,content-type,x-manufacturing-realtime-correlation-id");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData(AllowedOrigin)]
    [InlineData(LoopbackAllowedOrigin)]
    public async Task Each_configured_origin_receives_its_own_exact_cors_origin_value(string origin)
    {
        await using var fixture = await CorsFixture.CreateAsync();

        var response = await fixture.SendAsync(HttpMethod.Post, "/api/production/records/preview", origin, permissions: ["production.record"]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(origin, CorsOrigin(response));
    }

    [Fact]
    public async Task Allowed_origin_rate_limited_worker_photo_response_keeps_cors_headers()
    {
        await using var fixture = await CorsFixture.CreateAsync(photoPermitLimit: 1);

        var first = await fixture.SendAsync(HttpMethod.Get, "/api/workers/11111111-1111-1111-1111-111111111111/photo", AllowedOrigin, permissions: ["workers.view"]);
        var limited = await fixture.SendAsync(HttpMethod.Get, "/api/workers/11111111-1111-1111-1111-111111111111/photo", AllowedOrigin, permissions: ["workers.view"]);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(AllowedOrigin, CorsOrigin(first));
        Assert.Equal((HttpStatusCode)StatusCodes.Status429TooManyRequests, limited.StatusCode);
        Assert.Equal(AllowedOrigin, CorsOrigin(limited));
        Assert.True(limited.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Exhausting_photo_policy_does_not_consume_preview_policy()
    {
        await using var fixture = await CorsFixture.CreateAsync(photoPermitLimit: 1, productionPermitLimit: 1);

        var firstPhoto = await fixture.SendAsync(HttpMethod.Get, "/api/workers/11111111-1111-1111-1111-111111111111/photo", AllowedOrigin, permissions: ["workers.view"]);
        var limitedPhoto = await fixture.SendAsync(HttpMethod.Get, "/api/workers/11111111-1111-1111-1111-111111111111/photo", AllowedOrigin, permissions: ["workers.view"]);
        var preview = await fixture.SendAsync(HttpMethod.Post, "/api/production/records/preview", AllowedOrigin, permissions: ["production.record"]);

        Assert.Equal(HttpStatusCode.OK, firstPhoto.StatusCode);
        Assert.Equal((HttpStatusCode)StatusCodes.Status429TooManyRequests, limitedPhoto.StatusCode);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(AllowedOrigin, CorsOrigin(preview));
    }

    [Fact]
    public async Task Allowed_origin_unauthorized_and_forbidden_responses_keep_cors_headers()
    {
        await using var fixture = await CorsFixture.CreateAsync();

        var unauthorized = await fixture.SendAsync(HttpMethod.Post, "/api/production/records/preview", AllowedOrigin);
        var forbidden = await fixture.SendAsync(HttpMethod.Post, "/api/production/records/preview", AllowedOrigin, permissions: ["workers.view"]);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(AllowedOrigin, CorsOrigin(unauthorized));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(AllowedOrigin, CorsOrigin(forbidden));
    }

    [Fact]
    public async Task Allowed_origin_validation_and_conflict_responses_keep_cors_headers()
    {
        await using var fixture = await CorsFixture.CreateAsync();

        var validation = await fixture.SendAsync(HttpMethod.Post, "/api/production/records/validation", AllowedOrigin, permissions: ["production.record"]);
        var conflict = await fixture.SendAsync(HttpMethod.Post, "/api/production/records/conflict", AllowedOrigin, permissions: ["production.record"]);

        Assert.Equal(HttpStatusCode.BadRequest, validation.StatusCode);
        Assert.Equal(AllowedOrigin, CorsOrigin(validation));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(AllowedOrigin, CorsOrigin(conflict));
    }

    [Fact]
    public async Task Allowed_origin_preview_post_reaches_the_authorized_backend_and_disallowed_origin_is_not_granted_cors_access()
    {
        await using var fixture = await CorsFixture.CreateAsync();

        var preview = await fixture.SendAsync(HttpMethod.Post, "/api/production/records/preview", AllowedOrigin, permissions: ["production.record"]);
        var disallowedPreflight = await fixture.SendAsync(HttpMethod.Options, "/api/production/records/preview", DisallowedOrigin, accessControlRequestMethod: "POST");

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(AllowedOrigin, CorsOrigin(preview));
        Assert.False(disallowedPreflight.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static string? CorsOrigin(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.SingleOrDefault() : null;

    private sealed class CorsFixture : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private CorsFixture(WebApplication app)
        {
            _app = app;
            _client = app.GetTestClient();
        }

        public static async Task<CorsFixture> CreateAsync(int photoPermitLimit = 20, int productionPermitLimit = 20)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "IntegrationTest" });
            builder.WebHost.UseTestServer();
            var allowedOrigins = new[] { AllowedOrigin, LoopbackAllowedOrigin, LanAllowedOrigin };
            builder.Services.AddCors(options => options.AddPolicy("test-cors", policy => policy
                .SetIsOriginAllowed(origin => allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                .WithMethods("GET", "POST", "PATCH", "OPTIONS")
                .WithHeaders("Accept", "Authorization", "Content-Type", "X-Requested-With", "X-SignalR-User-Agent", ManufacturingCorrelationHeader, "X-Test-Permissions")
                .AllowCredentials()));
            builder.Services.AddSignalR();
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy("photo", _ => RateLimitPartition.GetFixedWindowLimiter("test-client", _ => FixedWindow(photoPermitLimit)));
                options.AddPolicy("production", _ => RateLimitPartition.GetFixedWindowLimiter("test-client", _ => FixedWindow(productionPermitLimit)));
                options.OnRejected = (context, _) =>
                {
                    context.HttpContext.Response.Headers.RetryAfter = "60";
                    return ValueTask.CompletedTask;
                };
            });
            builder.Services.AddAuthentication("test")
                .AddScheme<AuthenticationSchemeOptions, CorsHeaderAuthenticationHandler>("test", _ => { });
            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy("production.record", policy => policy.RequireAuthenticatedUser().RequireClaim("permission", "production.record"));
                options.AddPolicy("workers.view", policy => policy.RequireAuthenticatedUser().RequireClaim("permission", "workers.view"));
            });

            var app = builder.Build();
            app.UseCors("test-cors");
            app.UseAuthentication();
            app.UseRateLimiter();
            app.UseAuthorization();
            app.MapPost("/api/production/records/preview", () => Results.Ok(new { reached = true })).RequireAuthorization("production.record").RequireRateLimiting("production");
            app.MapPost("/api/production/records/validation", () => Results.ValidationProblem(new Dictionary<string, string[]> { ["productionDate"] = ["Production date is required."] })).RequireAuthorization("production.record");
            app.MapPost("/api/production/records/conflict", () => Results.Conflict()).RequireAuthorization("production.record");
            app.MapPatch("/api/product-models/{modelId:guid}/stages/{stageId:guid}", () => Results.Ok()).RequireAuthorization("production.record");
            app.MapGet("/api/workers/{workerId:guid}/photo", () => Results.Ok()).RequireAuthorization("workers.view").RequireRateLimiting("photo");
            app.MapHub<CorsTestNotificationsHub>("/hubs/notifications").RequireAuthorization();
            await app.StartAsync();
            return new CorsFixture(app);
        }

        public Task<HttpResponseMessage> SendAsync(
            HttpMethod method,
            string path,
            string origin,
            IReadOnlyCollection<string>? permissions = null,
            string? accessControlRequestMethod = null,
            string? accessControlRequestHeaders = null)
        {
            var request = new HttpRequestMessage(method, path);
            request.Headers.Add("Origin", origin);
            if (permissions is not null)
            {
                request.Headers.Add("X-Test-User", "11111111-1111-1111-1111-111111111111");
                request.Headers.Add("X-Test-Permissions", string.Join(',', permissions));
            }

            if (accessControlRequestMethod is not null)
            {
                request.Headers.Add("Access-Control-Request-Method", accessControlRequestMethod);
                request.Headers.Add("Access-Control-Request-Headers", accessControlRequestHeaders ?? "authorization,content-type");
            }

            return _client.SendAsync(request);
        }

        public ValueTask DisposeAsync() => _app.DisposeAsync();

        private static FixedWindowRateLimiterOptions FixedWindow(int permitLimit) => new()
        {
            PermitLimit = permitLimit,
            QueueLimit = 0,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        };
    }

    private sealed class CorsHeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = Request.Headers["X-Test-User"].ToString();
            if (!Guid.TryParse(userId, out _)) return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
            claims.AddRange(Request.Headers["X-Test-Permissions"].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(permission => new Claim("permission", permission)));
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class CorsTestNotificationsHub : Hub
    {
    }
}
