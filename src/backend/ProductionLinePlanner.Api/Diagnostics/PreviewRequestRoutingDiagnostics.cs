using System.Security.Claims;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ProductionLinePlanner.Api.Diagnostics;

/// <summary>
/// Development-only routing evidence for the draft-preview mutation. It is
/// deliberately limited to route metadata: no authorization token or request
/// body is captured.
/// </summary>
public static class PreviewRequestRoutingDiagnostics
{
    private const string PreviewPath = "/api/production/records/preview";

    public const string RequestIdHeader = "X-PLP-Request-Id";
    public const string EndpointHeader = "X-PLP-Endpoint";
    public const string SelectedMethodsHeader = "X-PLP-Selected-Methods";
    public const string CandidateMethodsHeader = "X-PLP-Candidate-Methods";

    public static IApplicationBuilder UsePreviewRequestRoutingDiagnostics(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        return app.Use(async (context, next) =>
        {
            if (!string.Equals(context.Request.Path.Value, PreviewPath, StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            // Endpoint routing has already selected an endpoint before the
            // middleware pipeline runs. Reading it here proves the endpoint
            // selected for the original HTTP method without changing routing.
            var endpoint = context.GetEndpoint();
            var endpointName = endpoint?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
                ?? endpoint?.DisplayName
                ?? "NoEndpoint";
            var routePattern = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? "NoRoutePattern";
            var selectedMethods = JoinMethods(endpoint?.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods);
            var candidateMethods = JoinMethods(context.RequestServices.GetServices<EndpointDataSource>()
                .SelectMany(dataSource => dataSource.Endpoints)
                .OfType<RouteEndpoint>()
                .Where(candidate => string.Equals(candidate.RoutePattern.RawText, PreviewPath, StringComparison.OrdinalIgnoreCase))
                .SelectMany(candidate => candidate.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? Array.Empty<string>()));
            var userMarker = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
            var origin = context.Request.Headers.Origin.ToString();

            context.Response.OnStarting(() =>
            {
                context.Response.Headers[RequestIdHeader] = context.TraceIdentifier;
                context.Response.Headers[EndpointHeader] = endpointName;
                context.Response.Headers[SelectedMethodsHeader] = selectedMethods;
                context.Response.Headers[CandidateMethodsHeader] = candidateMethods;
                return Task.CompletedTask;
            });

            app.Logger.LogInformation(
                "Preview routing diagnostic {CorrelationId} {Method} {Path} {Origin} {EndpointName} {RoutePattern} {SelectedMethods} {CandidateMethods} {UserMarker}",
                context.TraceIdentifier,
                context.Request.Method,
                context.Request.Path,
                string.IsNullOrWhiteSpace(origin) ? "no-origin" : origin,
                endpointName,
                routePattern,
                selectedMethods,
                candidateMethods,
                userMarker);

            await next();
        });
    }

    private static string JoinMethods(IEnumerable<string>? methods) => string.Join(',', (methods ?? Array.Empty<string>())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(method => method, StringComparer.OrdinalIgnoreCase));
}
