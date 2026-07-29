using ProductionLinePlanner.Api.Authorization;
using ProductionLinePlanner.Api.Diagnostics;
using ProductionLinePlanner.Api.Security;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Notifications;
using ProductionLinePlanner.Domain.Authorization;

namespace ProductionLinePlanner.Api.Endpoints;

public static class NotificationPolicyAdminEndpoints
{
    public static void MapNotificationPolicyAdminEndpoints(this WebApplication app)
    {
        var policyApi = app.MapGroup("/api/admin/notification-policies")
            .RequireAuthorization()
            .RequirePermission(NotificationPolicyPermissions.Manage);

        policyApi.MapGet("/foundation", (INotificationPolicyAdminService adminService) =>
            Results.Ok(ApiResponse.Success(adminService.GetFoundation())))
            .RequireRateLimiting(ApiRateLimitPolicies.NormalRead)
            .WithTags("Notification Policies")
            .WithName("GetNotificationPolicyStudioFoundation");

        policyApi.MapGet("", async (
            INotificationPolicyAdminService adminService,
            CancellationToken cancellationToken) =>
        {
            var result = await adminService.GetPoliciesAsync(cancellationToken);
            return result.IsSuccess
                ? Results.Ok(ApiResponse.Success(result.Value!))
                : ApiResponse.Failure(result.Error?.Code ?? "NotificationPolicyReadFailed", result.Error?.Message ?? "Unable to load notification policies.", MapFailureStatus(result.Error?.Code));
        })
            .RequireRateLimiting(ApiRateLimitPolicies.NormalRead)
            .WithTags("Notification Policies")
            .WithName("ListNotificationPolicies");

        policyApi.MapGet("/recipient-options", async (
            INotificationPolicyAdminService adminService,
            CancellationToken cancellationToken) =>
        {
            var result = await adminService.GetRecipientOptionsAsync(cancellationToken);
            return result.IsSuccess
                ? Results.Ok(ApiResponse.Success(result.Value!))
                : ApiResponse.Failure(result.Error?.Code ?? "NotificationRecipientOptionsReadFailed", result.Error?.Message ?? "Unable to load recipient options.", MapFailureStatus(result.Error?.Code));
        })
            .RequireRateLimiting(ApiRateLimitPolicies.NormalRead)
            .WithTags("Notification Policies")
            .WithName("GetNotificationPolicyRecipientOptions");

        policyApi.MapGet("/{eventKey}", async (
            string eventKey,
            INotificationPolicyAdminService adminService,
            CancellationToken cancellationToken) =>
        {
            var result = await adminService.GetPolicyAsync(eventKey, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(ApiResponse.Success(result.Value!))
                : ApiResponse.Failure(result.Error?.Code ?? "NotificationPolicyReadFailed", result.Error?.Message ?? "Unable to load notification policy.", MapFailureStatus(result.Error?.Code));
        })
            .RequireRateLimiting(ApiRateLimitPolicies.NormalRead)
            .WithTags("Notification Policies")
            .WithName("GetNotificationPolicy");

        policyApi.MapPut("/{eventKey}", async (
            string eventKey,
            NotificationPolicyUpdateRequest request,
            INotificationPolicyAdminService adminService,
            ICurrentUserService currentUserService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (currentUserService.UserId is not Guid actorUserId)
            {
                return ApiResponse.Failure("Unauthorized", "User context is required.", 401);
            }

            var result = await adminService.UpdatePolicyAsync(
                eventKey,
                request,
                actorUserId,
                AuditRequestMetadata.From(httpContext),
                cancellationToken);
            return result.IsSuccess
                ? Results.Ok(ApiResponse.Success(result.Value!))
                : ApiResponse.Failure(result.Error?.Code ?? "NotificationPolicyUpdateFailed", result.Error?.Message ?? "Unable to update notification policy.", MapFailureStatus(result.Error?.Code));
        })
            .RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite)
            .WithTags("Notification Policies")
            .WithName("UpdateNotificationPolicy");

        policyApi.MapPut("/{eventKey}/recipient-rules", async (
            string eventKey,
            NotificationPolicyRecipientRulesReplaceRequest request,
            INotificationPolicyAdminService adminService,
            ICurrentUserService currentUserService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (currentUserService.UserId is not Guid actorUserId)
            {
                return ApiResponse.Failure("Unauthorized", "User context is required.", 401);
            }

            var result = await adminService.ReplaceRecipientRulesAsync(
                eventKey,
                request,
                actorUserId,
                AuditRequestMetadata.From(httpContext),
                cancellationToken);
            return result.IsSuccess
                ? Results.Ok(ApiResponse.Success(result.Value!))
                : ApiResponse.Failure(result.Error?.Code ?? "NotificationRecipientRulesUpdateFailed", result.Error?.Message ?? "Unable to update recipient rules.", MapFailureStatus(result.Error?.Code));
        })
            .RequireRateLimiting(ApiRateLimitPolicies.CriticalProductionWrite)
            .WithTags("Notification Policies")
            .WithName("ReplaceNotificationPolicyRecipientRules");
    }

    private static int MapFailureStatus(string? code) => code switch
    {
        "Unauthorized" => StatusCodes.Status401Unauthorized,
        "UnknownNotificationEvent" or "NotificationPolicyNotFound" => StatusCodes.Status404NotFound,
        "ConcurrencyConflict" => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    };
}
