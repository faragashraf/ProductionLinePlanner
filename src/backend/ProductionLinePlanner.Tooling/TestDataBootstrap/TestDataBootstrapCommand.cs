using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace ProductionLinePlanner.Tooling.TestDataBootstrap;

public static class TestDataBootstrapCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 2 || !args[0].Equals("test-data", StringComparison.OrdinalIgnoreCase))
        {
            PrintUsage();
            return 64;
        }

        var mode = args[1].ToLowerInvariant();
        var options = TestDataBootstrapOptions.Create(mode);

        try
        {
            return mode switch
            {
                "preflight" => await RunPreflightAsync(options),
                "apply" => await new TestDataCopyService(options).ApplyAsync(CancellationToken.None),
                "verify" => await new TestDataVerificationService(options).VerifyAsync(CancellationToken.None),
                _ => PrintUsage()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ToSanitizedOperatorError(mode, ex));
            return 1;
        }
    }

    private static async Task<int> RunPreflightAsync(TestDataBootstrapOptions options)
    {
        var report = await new TestDataPreflightService(options).RunAsync(writeReport: true, CancellationToken.None);
        Console.WriteLine($"Preflight result: {report.OverallResult}");
        Console.WriteLine($"Sanitized report: {Path.GetRelativePath(options.RepoRoot, options.PreflightReportPath)}");
        Console.WriteLine($"SOURCE_TEST_DB: SQL major {report.Source.ProductMajorVersion}, compatibility {report.Source.CompatibilityLevel}");
        Console.WriteLine($"TARGET_SQL2016_DB: SQL major {report.Target.ProductMajorVersion}, compatibility {report.Target.CompatibilityLevel}");
        Console.WriteLine("Included source rows:");
        foreach (var table in report.Tables.Where(x => x.Decision == "Include").OrderBy(x => x.Phase).ThenBy(x => x.Table))
        {
            Console.WriteLine($"  {table.Table}: {table.SourceRows}");
        }

        if (report.Warnings.Count > 0)
        {
            Console.WriteLine("Warnings:");
            foreach (var warning in report.Warnings)
            {
                Console.WriteLine($"  {warning}");
            }
        }

        if (report.Blockers.Count > 0)
        {
            Console.Error.WriteLine("Blockers:");
            foreach (var blocker in report.Blockers)
            {
                Console.Error.WriteLine($"  {blocker}");
            }
        }

        return report.OverallResult == "Passed" ? 0 : 2;
    }

    private static int PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  dotnet run --project src/backend/ProductionLinePlanner.Tooling -- test-data preflight");
        Console.Error.WriteLine("  dotnet run --project src/backend/ProductionLinePlanner.Tooling -- test-data apply");
        Console.Error.WriteLine("  dotnet run --project src/backend/ProductionLinePlanner.Tooling -- test-data verify");
        return 64;
    }

    private static string ToSanitizedOperatorError(string mode, Exception exception)
    {
        var category = exception switch
        {
            JsonException => "MalformedReport",
            InvalidOperationException invalidOperation when invalidOperation.Message.Contains("ConnectionStrings:", StringComparison.OrdinalIgnoreCase) => "ConfigurationFailure",
            SqlException => "SqlConnectivityOrExecutionFailure",
            TimeoutException => "TimeoutFailure",
            OperationCanceledException => "Cancelled",
            _ => mode switch
            {
                "preflight" => "PreflightFailure",
                "apply" => "ApplyFailure",
                "verify" => "VerificationFailure",
                _ => "UnexpectedFailure"
            }
        };

        return $"Test Data Bootstrap failed closed. Operation={mode}; Category={category}. No connection or row data was printed. Inspect local diagnostics and rerun after correcting the cause.";
    }
}
