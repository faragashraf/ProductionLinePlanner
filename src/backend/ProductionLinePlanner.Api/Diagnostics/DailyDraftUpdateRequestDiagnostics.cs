using System.Diagnostics;
using Microsoft.AspNetCore.Routing;

namespace ProductionLinePlanner.Api.Diagnostics;

/// <summary>
/// Debug-level routing evidence for the daily-draft item PUT only. The
/// middleware never reads the request body and never records identities,
/// authorization headers, or worker payloads.
/// </summary>
public static class DailyDraftUpdateRequestDiagnostics
{
    public const string CorrelationHeader = "X-Manufacturing-Realtime-Correlation-Id";
    private const string PathPrefix = "/api/production/daily-operations/drafts/";

    public static IApplicationBuilder UseDailyDraftUpdateRequestDiagnostics(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsPut(context.Request.Method) ||
                context.Request.Path.Value?.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase) != true)
            {
                await next();
                return;
            }

            var endpoint = context.GetEndpoint();
            var displayName = endpoint?.DisplayName ?? "NoEndpoint";
            var routePattern = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? "NoRoutePattern";
            var correlationId = context.Request.Headers[CorrelationHeader].ToString();
            var stopwatch = Stopwatch.StartNew();

            app.Logger.LogDebug(
                "Daily draft PUT started {TraceIdentifier} {CorrelationId} {Method} {Path} {EndpointDisplayName} {RoutePattern}",
                context.TraceIdentifier,
                string.IsNullOrWhiteSpace(correlationId) ? "no-correlation" : correlationId,
                context.Request.Method,
                context.Request.Path,
                displayName,
                routePattern);

            try
            {
                await next();
            }
            finally
            {
                app.Logger.LogDebug(
                    "Daily draft PUT completed {TraceIdentifier} {CorrelationId} {Method} {Path} {EndpointDisplayName} {RoutePattern} {StatusCode} {AllowHeader} {ElapsedMilliseconds}",
                    context.TraceIdentifier,
                    string.IsNullOrWhiteSpace(correlationId) ? "no-correlation" : correlationId,
                    context.Request.Method,
                    context.Request.Path,
                    displayName,
                    routePattern,
                    context.Response.StatusCode,
                    context.Response.Headers.Allow.ToString(),
                    stopwatch.ElapsedMilliseconds);
            }
        });
    }
}
