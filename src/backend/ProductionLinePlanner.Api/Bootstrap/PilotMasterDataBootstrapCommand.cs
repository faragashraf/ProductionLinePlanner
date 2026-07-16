using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Api.Bootstrap;

/// <summary>
/// Explicit development command for the first pilot master-data bootstrap.
/// It intentionally uses fixed local source paths and never exposes workbook contents.
/// </summary>
public static class PilotMasterDataBootstrapCommand
{
    private const string BootstrapCommandName = "--pilot-master-bootstrap";
    private const string ResetCommandName = "--pilot-master-reset";
    private const string VerifyCommandName = "--pilot-master-verify";
    private const string StagesPath = "/Users/ashraffarag/Downloads/المراحل.xlsx";
    private const string SalaryPath = "/Users/ashraffarag/Downloads/الراتب الاساسي - الاقسام.xlsx";

    public static bool IsRequested(string[] args) =>
        args.Any(x => x.StartsWith($"{BootstrapCommandName}=", StringComparison.Ordinal) ||
                      x.StartsWith($"{ResetCommandName}=", StringComparison.Ordinal) ||
                      string.Equals(x, VerifyCommandName, StringComparison.Ordinal));

    public static async Task ExecuteAsync(WebApplication app, string[] args)
    {
        if (!app.Environment.IsDevelopment())
        {
            throw new InvalidOperationException("Pilot master-data bootstrap is available only in Development.");
        }

        var resetMode = Value(args, ResetCommandName)?.Trim().ToLowerInvariant();
        if (resetMode is not null)
        {
            await ExecuteResetAsync(app, args, resetMode);
            return;
        }

        var isVerification = args.Contains(VerifyCommandName, StringComparer.Ordinal);
        var mode = isVerification ? "verify" : Value(args, BootstrapCommandName)?.Trim().ToLowerInvariant();
        if (mode is not "dry-run" and not "apply" and not "verify")
        {
            throw new InvalidOperationException("Use --pilot-master-bootstrap=dry-run, --pilot-master-bootstrap=apply, or --pilot-master-verify.");
        }

        if (!File.Exists(StagesPath) || !File.Exists(SalaryPath))
        {
            throw new FileNotFoundException("The controlled stages or salary workbook is not available at its required local path.");
        }

        CompensationMode? compensationMode = null;
        var modeValue = Value(args, "--compensation-mode");
        if (!string.IsNullOrWhiteSpace(modeValue))
        {
            if (!Enum.TryParse<CompensationMode>(modeValue, true, out var parsedMode) || !Enum.IsDefined(parsedMode))
            {
                throw new InvalidOperationException("--compensation-mode must be an existing CompensationMode enum value.");
            }
            compensationMode = parsedMode;
        }

        var input = new PilotMasterDataBootstrapInput(
            await File.ReadAllBytesAsync(StagesPath),
            await File.ReadAllBytesAsync(SalaryPath),
            // The production workbook is intentionally not opened or imported by this command.
            ProductionWorkbookVerified: true,
            ExplicitCompensationMode: compensationMode);

        await using var scope = app.Services.CreateAsyncScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<IPilotMasterDataBootstrapService>();
        object result;
        if (mode == "dry-run")
        {
            result = await bootstrap.PreviewAsync(input);
        }
        else if (mode == "verify")
        {
            result = await bootstrap.VerifyAsync(input);
        }
        else
        {
            if (!args.Contains("--confirm-apply", StringComparer.Ordinal))
            {
                throw new InvalidOperationException("Apply requires --confirm-apply after reviewing the dry run.");
            }
            var actorUserId = await ResolveActorUserIdAsync(scope.ServiceProvider, args);
            result = await bootstrap.ApplyAsync(input, actorUserId, confirmed: true);
        }

        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task ExecuteResetAsync(WebApplication app, string[] args, string mode)
    {
        if (mode is not "dry-run" and not "apply")
        {
            throw new InvalidOperationException("Use --pilot-master-reset=dry-run or --pilot-master-reset=apply.");
        }

        await using var scope = app.Services.CreateAsyncScope();
        var reset = scope.ServiceProvider.GetRequiredService<IPilotMasterDataResetService>();
        object result;
        if (mode == "dry-run")
        {
            result = await reset.PreviewAsync();
        }
        else
        {
            if (!args.Contains("--confirm-reset", StringComparer.Ordinal))
            {
                throw new InvalidOperationException("Reset apply requires --confirm-reset after reviewing the reset dry run.");
            }
            var actorUserId = await ResolveActorUserIdAsync(scope.ServiceProvider, args);
            result = await reset.ApplyAsync(actorUserId, confirmed: true);
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
            throw new InvalidOperationException("Apply requires --actor-user-id=<active Super Admin GUID> or --actor-user-id=auto when exactly one active Super Admin exists.");
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
