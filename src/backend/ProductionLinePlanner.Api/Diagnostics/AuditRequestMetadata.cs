using Microsoft.AspNetCore.Http;

namespace ProductionLinePlanner.Api.Diagnostics;

public static class AuditRequestMetadata
{
    public static string From(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var metadata = $"{httpContext.Request.Method} {httpContext.Request.Path}";
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        return string.IsNullOrWhiteSpace(ipAddress)
            ? metadata
            : $"{metadata} from {ipAddress}";
    }
}
