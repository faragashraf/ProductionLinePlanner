using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
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
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=ProductionLinePlannerDesignTime;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;

        return new AppDbContext(options);
    }
}
