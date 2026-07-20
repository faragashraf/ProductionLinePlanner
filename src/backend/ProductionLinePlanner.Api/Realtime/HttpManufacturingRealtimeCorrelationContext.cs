using ProductionLinePlanner.Application.Realtime;

namespace ProductionLinePlanner.Api.Realtime;

public sealed class HttpManufacturingRealtimeCorrelationContext(IHttpContextAccessor httpContextAccessor) : IManufacturingRealtimeCorrelationContext
{
    public string? CorrelationId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.Request.Headers["X-Manufacturing-Realtime-Correlation-Id"].ToString().Trim();
            return Guid.TryParse(value, out var correlationId) ? correlationId.ToString("D") : null;
        }
    }
}
