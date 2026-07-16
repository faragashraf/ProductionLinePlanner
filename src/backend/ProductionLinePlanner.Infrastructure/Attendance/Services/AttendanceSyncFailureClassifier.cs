using Microsoft.Data.SqlClient;

namespace ProductionLinePlanner.Infrastructure.Attendance.Services;

public static class AttendanceSyncFailureClassifier
{
    public const string ClientCancelled = "AttendanceSyncClientCancelled";
    public const string Cancelled = "AttendanceSyncCancelled";
    public const string InternalTimeout = "AttendanceSyncTimeout";
    public const string SourceTimeout = "AttendanceSourceTimeout";

    public static string? Classify(Exception exception, bool requestTokenCancelled, bool internalTimeoutCancelled)
    {
        if (exception is OperationCanceledException && requestTokenCancelled) return ClientCancelled;
        if (exception is OperationCanceledException && internalTimeoutCancelled) return InternalTimeout;
        if (exception is SqlException { Number: -2 } || exception is TimeoutException) return SourceTimeout;
        return null;
    }
}
