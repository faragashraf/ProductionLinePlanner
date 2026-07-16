namespace ProductionLinePlanner.Tests;

public sealed class TestDataBootstrapVerificationSqlTests
{
    [Fact]
    public void Focused_unique_key_verification_uses_production_line_database_column()
    {
        var source = File.ReadAllText(FindRepoFile("src/backend/ProductionLinePlanner.Tooling/TestDataBootstrap/TestDataVerificationService.cs"));

        Assert.Contains("LineCode FROM dbo.ProductionLines", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT Code FROM dbo.ProductionLines", source, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {relativePath}");
    }
}
