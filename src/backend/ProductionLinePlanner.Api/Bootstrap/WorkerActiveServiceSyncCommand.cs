using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Api.Bootstrap;

/// <summary>Development-only command for the one-time current-service worker projection correction.</summary>
public static class WorkerActiveServiceSyncCommand
{
    private const string CommandName = "--worker-active-service-sync";

    public static bool IsRequested(IEnumerable<string> args) =>
        args.Any(x => x.StartsWith($"{CommandName}=", StringComparison.Ordinal));

    public static async Task ExecuteAsync(WebApplication app, string[] args)
    {
        if (!app.Environment.IsDevelopment())
        {
            throw new InvalidOperationException("Active-service worker synchronization is available only in Development.");
        }

        var mode = Value(args, CommandName)?.Trim().ToLowerInvariant();
        if (mode is not "dry-run" and not "apply")
        {
            throw new InvalidOperationException("Use --worker-active-service-sync=dry-run or --worker-active-service-sync=apply.");
        }

        await using var scope = app.Services.CreateAsyncScope();
        var workerSync = scope.ServiceProvider.GetRequiredService<IWorkerInitialSyncService>();
        object result;
        if (mode == "dry-run")
        {
            result = await workerSync.PreviewActiveServiceSyncAsync();
        }
        else
        {
            if (!args.Contains("--confirm-apply", StringComparer.Ordinal))
            {
                throw new InvalidOperationException("Apply requires --confirm-apply after reviewing the dry run.");
            }

            var actorUserId = await ResolveActorUserIdAsync(scope.ServiceProvider, args);
            var workerResult = await workerSync.SyncWorkersAsync(actorUserId, "Development-only active-service worker projection correction");
            if (workerResult.IsFailure)
            {
                throw new InvalidOperationException(workerResult.Error?.Message ?? "Worker synchronization failed.");
            }

            var productionDate = DateOnly.TryParse(Value(args, "--production-date"), out var selectedDate)
                ? selectedDate
                : DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")));
            var attendanceSync = scope.ServiceProvider.GetRequiredService<IAttendanceSyncService>();
            var attendanceResult = await attendanceSync.SyncForProductionDateAsync(productionDate);
            object attendancePayload = attendanceResult.IsSuccess
                ? attendanceResult.Value!
                : new { Error = attendanceResult.Error?.Code };
            result = new
            {
                WorkerSynchronization = workerResult.Value,
                AttendanceRefresh = attendancePayload
            };
        }

        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string? Value(IEnumerable<string> args, string name) =>
        args.FirstOrDefault(x => x.StartsWith($"{name}=", StringComparison.Ordinal))?[($"{name}=").Length..];

    private static async Task<Guid> ResolveActorUserIdAsync(IServiceProvider services, IEnumerable<string> args)
    {
        var suppliedValue = Value(args, "--actor-user-id");
        if (Guid.TryParse(suppliedValue, out var suppliedId))
        {
            return suppliedId;
        }
        if (!string.Equals(suppliedValue, "auto", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Apply requires --actor-user-id=<active Super Admin GUID> or --actor-user-id=auto.");
        }

        var db = services.GetRequiredService<AppDbContext>();
        var candidates = await db.AppUsers.AsNoTracking().Include(x => x.Roles)
            .Where(x => x.IsActive && x.Roles.Any(role => role.IsActive && role.Role == UserRole.SuperAdmin))
            .Select(x => x.Id)
            .ToArrayAsync();
        if (candidates.Length != 1)
        {
            throw new InvalidOperationException("Automatic actor selection requires exactly one active Super Admin.");
        }
        return candidates[0];
    }
}
