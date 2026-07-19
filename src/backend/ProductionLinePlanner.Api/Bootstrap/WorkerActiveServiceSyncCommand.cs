using System.Text.Json;
using ProductionLinePlanner.Application.Services;

namespace ProductionLinePlanner.Api.Bootstrap;

/// <summary>Development-only, read-only worker master synchronization preview.</summary>
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
        if (mode is not "dry-run")
        {
            throw new InvalidOperationException("Worker sync apply is disabled in this foundation. Use --worker-active-service-sync=dry-run.");
        }

        await using var scope = app.Services.CreateAsyncScope();
        var workerSync = scope.ServiceProvider.GetRequiredService<IWorkerInitialSyncService>();
        var result = await workerSync.PreviewActiveServiceSyncAsync();
        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Error?.Message ?? "Worker synchronization preview failed.");
        }

        Console.WriteLine(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string? Value(IEnumerable<string> args, string name) =>
        args.FirstOrDefault(x => x.StartsWith($"{name}=", StringComparison.Ordinal))?[($"{name}=").Length..];

}
