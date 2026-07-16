using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Api.Diagnostics;

namespace ProductionLinePlanner.Tests;

public sealed class PreviewRequestRoutingDiagnosticsIntegrationTests
{
    [Fact]
    public async Task Authenticated_preview_post_selects_the_named_post_endpoint_and_preserves_routing_evidence()
    {
        await using var fixture = await PreviewDiagnosticsFixture.CreateAsync();

        var response = await fixture.SendAsync(HttpMethod.Post, "/api/production/records/preview", authenticated: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("CalculateStageProductionRecordPreview", Header(response, PreviewRequestRoutingDiagnostics.EndpointHeader));
        Assert.Equal("POST", Header(response, PreviewRequestRoutingDiagnostics.SelectedMethodsHeader));
        Assert.Equal("POST", Header(response, PreviewRequestRoutingDiagnostics.CandidateMethodsHeader));
        Assert.False(string.IsNullOrWhiteSpace(Header(response, PreviewRequestRoutingDiagnostics.RequestIdHeader)));
    }

    [Fact]
    public async Task Get_preview_keeps_the_post_candidate_evidence_when_routing_returns_method_not_allowed()
    {
        await using var fixture = await PreviewDiagnosticsFixture.CreateAsync();

        var response = await fixture.SendAsync(HttpMethod.Get, "/api/production/records/preview", authenticated: true);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("405 HTTP Method Not Supported", Header(response, PreviewRequestRoutingDiagnostics.EndpointHeader));
        Assert.Equal("POST", Header(response, PreviewRequestRoutingDiagnostics.CandidateMethodsHeader));
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.SingleOrDefault() : null;

    private sealed class PreviewDiagnosticsFixture : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private PreviewDiagnosticsFixture(WebApplication app)
        {
            _app = app;
            _client = app.GetTestClient();
        }

        public static async Task<PreviewDiagnosticsFixture> CreateAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseTestServer();
            builder.Services.AddAuthentication("test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("test", _ => { });
            builder.Services.AddAuthorization();

            var app = builder.Build();
            app.UseAuthentication();
            app.UsePreviewRequestRoutingDiagnostics();
            app.UseAuthorization();
            app.MapPost("/api/production/records/preview", () => Results.Ok())
                .RequireAuthorization()
                .WithName("CalculateStageProductionRecordPreview");
            await app.StartAsync();
            return new PreviewDiagnosticsFixture(app);
        }

        public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, bool authenticated)
        {
            var request = new HttpRequestMessage(method, path);
            if (authenticated)
            {
                request.Headers.Add("X-Test-User", "11111111-1111-1111-1111-111111111111");
            }

            return _client.SendAsync(request);
        }

        public ValueTask DisposeAsync() => _app.DisposeAsync();
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Guid.TryParse(Request.Headers["X-Test-User"].ToString(), out var userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
