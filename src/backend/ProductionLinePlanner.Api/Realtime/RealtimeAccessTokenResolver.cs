using Microsoft.AspNetCore.Http;

namespace ProductionLinePlanner.Api.Realtime;

public static class RealtimeAccessTokenResolver
{
    public static string? Resolve(PathString requestPath, IQueryCollection query)
    {
        if (!requestPath.StartsWithSegments(RealtimeEndpointPaths.NotificationsHub))
        {
            return null;
        }

        var accessToken = query["access_token"].ToString();
        return string.IsNullOrWhiteSpace(accessToken) ? null : accessToken;
    }
}
