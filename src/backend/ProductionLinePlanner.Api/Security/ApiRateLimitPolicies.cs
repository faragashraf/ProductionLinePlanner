namespace ProductionLinePlanner.Api.Security;

public static class ApiRateLimitPolicies
{
    public const string CriticalProductionWrite = "critical-production-write";
    public const string WorkerPhotoRead = "worker-photo-read";
    public const string NormalRead = "normal-read";
}
