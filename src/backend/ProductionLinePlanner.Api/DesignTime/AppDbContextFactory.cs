using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Api.DesignTime;

/// <summary>
/// Supplies EF tooling with the AppDbContext model without hosting runtime services,
/// loading secrets, running seed jobs, or opening a database connection.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables();

        if (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
        {
            configuration.AddUserSecrets(typeof(AppDbContextFactory).Assembly);
        }

        var resolvedConfiguration = configuration.Build();
        var connectionString = resolvedConfiguration.GetConnectionString("AppDatabase");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Missing required connection string 'ConnectionStrings:AppDatabase'.");
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
