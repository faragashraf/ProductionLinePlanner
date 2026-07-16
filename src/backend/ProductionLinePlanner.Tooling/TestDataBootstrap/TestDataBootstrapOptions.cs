namespace ProductionLinePlanner.Tooling.TestDataBootstrap;

public sealed record TestDataBootstrapOptions(
    string Mode,
    string RepoRoot,
    string ReportDirectory,
    string PreflightReportPath,
    string VerificationReportPath)
{
    public static TestDataBootstrapOptions Create(string mode)
    {
        var repoRoot = FindRepoRoot();
        var reportDirectory = Path.Combine(repoRoot, "artifacts", "test-data-bootstrap");
        return new TestDataBootstrapOptions(
            mode,
            repoRoot,
            reportDirectory,
            Path.Combine(reportDirectory, "preflight-report.json"),
            Path.Combine(reportDirectory, "verification-report.json"));
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
