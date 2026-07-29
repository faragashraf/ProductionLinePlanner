using ProductionLinePlanner.Api.Authorization;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;

namespace ProductionLinePlanner.Api.Endpoints;

public static class AttendanceWorkforceEndpoints
{
    public static RouteGroupBuilder MapWorkerAttendanceHistoryEndpoints(this RouteGroupBuilder workersApi)
    {
        workersApi.MapGet("/{workerId:guid}/attendance-records", GetHistoryAsync)
            .RequirePermission("attendance.view")
            .WithTags("Workers", "Attendance")
            .WithName("GetWorkerAttendanceHistory");

        return workersApi;
    }

    public static RouteGroupBuilder MapAttendanceWorkforceEndpoints(this RouteGroupBuilder attendanceApi)
    {
        attendanceApi.MapPost("/sync/production-date/{productionDate}", SyncProductionDateAsync)
            .RequirePermission("attendance.sync")
            .WithTags("Attendance")
            .WithName("SyncAttendanceForProductionDate");

        attendanceApi.MapGet("/workforce", GetPageAsync)
            .RequirePermission("attendance.view")
            .RequirePermission("assignments.view")
            .WithTags("Attendance")
            .WithName("GetAttendanceWorkforce");

        attendanceApi.MapGet("/workforce/workers/{workerId:guid}/details", GetDetailAsync)
            .RequirePermission("attendance.view")
            .RequirePermission("assignments.view")
            .WithTags("Attendance")
            .WithName("GetAttendanceWorkforceDetail");

        attendanceApi.MapGet("/workforce/workers/{workerId:guid}/summary", GetProfileSummaryAsync)
            .RequirePermission("attendance.view")
            .WithTags("Attendance")
            .WithName("GetWorkerAttendanceProfileSummary");

        return attendanceApi;
    }

    private static async Task<IResult> SyncProductionDateAsync(
        DateOnly productionDate,
        IAttendanceEngine attendanceEngine,
        CancellationToken cancellationToken)
    {
        var result = await attendanceEngine.SyncForProductionDateAsync(productionDate, cancellationToken);
        return result.IsFailure
            ? ApiResponse.Failure(result.Error?.Code ?? "AttendanceSyncFailed", result.Error?.Message ?? "Unable to sync attendance data.", MapFailureStatusCode(result.Error?.Code))
            : Results.Ok(ApiResponse.Success(result.Value));
    }

    private static async Task<IResult> GetPageAsync(
        IAttendanceWorkforceEngine workforceEngine,
        ICairoTimeZoneProvider cairoTimeZoneProvider,
        DateOnly? productionDate = null,
        int page = 1,
        int pageSize = 25,
        string? search = null,
        Guid? factoryId = null,
        Guid? productionLineId = null,
        Guid? mainStageId = null,
        Guid? subStageId = null,
        string? department = null,
        string? attendanceFilter = null,
        string? assignmentFilter = null,
        string? operationalFilter = null,
        string? sortBy = null,
        string? sortDirection = null,
        CancellationToken cancellationToken = default)
    {
        var cairoDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cairoTimeZoneProvider.TimeZone));
        var result = await workforceEngine.GetPageAsync(new AttendanceWorkforceQuery(productionDate ?? cairoDate, page, pageSize, search, factoryId, productionLineId, mainStageId, subStageId, department, attendanceFilter, assignmentFilter, operationalFilter, sortBy, sortDirection), cancellationToken);
        return result.IsFailure
            ? ApiResponse.Failure(result.Error?.Code ?? "AttendanceWorkforceReadFailed", result.Error?.Message ?? "Unable to load workforce attendance.", MapFailureStatusCode(result.Error?.Code))
            : Results.Ok(ApiResponse.Success(result.Value!));
    }

    private static async Task<IResult> GetDetailAsync(
        Guid workerId,
        IAttendanceWorkforceEngine workforceEngine,
        ICairoTimeZoneProvider cairoTimeZoneProvider,
        DateOnly? productionDate = null,
        CancellationToken cancellationToken = default)
    {
        var cairoDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cairoTimeZoneProvider.TimeZone));
        var result = await workforceEngine.GetWorkerDetailAsync(workerId, productionDate ?? cairoDate, cancellationToken);
        return result.IsFailure
            ? ApiResponse.Failure(result.Error?.Code ?? "AttendanceWorkforceReadFailed", result.Error?.Message ?? "Unable to load worker attendance.", MapFailureStatusCode(result.Error?.Code))
            : Results.Ok(ApiResponse.Success(result.Value!));
    }

    private static async Task<IResult> GetProfileSummaryAsync(
        Guid workerId,
        IAttendanceWorkforceEngine workforceEngine,
        ICairoTimeZoneProvider cairoTimeZoneProvider,
        DateOnly? productionDate = null,
        CancellationToken cancellationToken = default)
    {
        var cairoDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cairoTimeZoneProvider.TimeZone));
        var result = await workforceEngine.GetWorkerProfileSummaryAsync(workerId, productionDate ?? cairoDate, cancellationToken);
        return result.IsFailure
            ? ApiResponse.Failure(result.Error?.Code ?? "AttendanceWorkforceReadFailed", result.Error?.Message ?? "Unable to load worker attendance summary.", MapFailureStatusCode(result.Error?.Code))
            : Results.Ok(ApiResponse.Success(result.Value!));
    }

    private static async Task<IResult> GetHistoryAsync(
        Guid workerId,
        IAttendanceWorkforceEngine workforceEngine,
        ICairoTimeZoneProvider cairoTimeZoneProvider,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        int page = 1,
        int pageSize = 20,
        string sortDirection = "desc",
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cairoTimeZoneProvider.TimeZone));
        var effectiveTo = toDate ?? today;
        var effectiveFrom = fromDate ?? effectiveTo.AddDays(-29);
        var result = await workforceEngine.GetWorkerAttendanceHistoryAsync(
            workerId,
            new WorkerAttendanceHistoryQuery(effectiveFrom, effectiveTo, page, pageSize, sortDirection),
            cancellationToken);
        return result.IsFailure
            ? ApiResponse.Failure(result.Error?.Code ?? "AttendanceWorkforceReadFailed", result.Error?.Message ?? "Unable to load worker attendance history.", MapFailureStatusCode(result.Error?.Code))
            : Results.Ok(ApiResponse.Success(result.Value!));
    }

    private static int MapFailureStatusCode(string? code) => code switch
    {
        "ValidationError" => StatusCodes.Status400BadRequest,
        "NotFound" => StatusCodes.Status404NotFound,
        "Unauthorized" or "InvalidToken" or "InvalidCredentials" => StatusCodes.Status401Unauthorized,
        "Conflict" or "AttendanceSyncInProgress" => StatusCodes.Status409Conflict,
        "Forbidden" => StatusCodes.Status403Forbidden,
        "AttendanceSyncTimeout" or "AttendanceSourceTimeout" => StatusCodes.Status504GatewayTimeout,
        "AttendanceSourceError" or "AttendanceSyncCancelled" => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError
    };
}
