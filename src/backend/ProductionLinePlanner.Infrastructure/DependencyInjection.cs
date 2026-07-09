using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var appConnectionString = configuration.GetConnectionString("AppDatabase")
            ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:AppDatabase' is required.");

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlServer(appConnectionString);
        });

        return services;
    }
}
