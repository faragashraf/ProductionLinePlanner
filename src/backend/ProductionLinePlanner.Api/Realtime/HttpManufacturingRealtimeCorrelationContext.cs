using ProductionLinePlanner.Application.Realtime;

namespace ProductionLinePlanner.Api.Realtime;

public static class ManufacturingRealtimeHeaders
{
    public const string CorrelationId = "X-Manufacturing-Realtime-Correlation-Id";
}

public sealed class HttpManufacturingRealtimeCorrelationContext(IHttpContextAccessor httpContextAccessor) : IManufacturingRealtimeCorrelationContext
{
    public string? CorrelationId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.Request.Headers[ManufacturingRealtimeHeaders.CorrelationId].ToString().Trim();
            return Guid.TryParse(value, out var correlationId) ? correlationId.ToString("D") : null;
        }
    }
}
