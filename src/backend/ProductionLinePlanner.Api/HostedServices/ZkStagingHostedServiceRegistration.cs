using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProductionLinePlanner.Infrastructure.Attendance;

namespace ProductionLinePlanner.Api.HostedServices;

/// <summary>
/// Keeps the startup registration decision aligned with the normalized AttendanceSource options.
/// This avoids Program reading raw configuration differently from Infrastructure binding.
/// </summary>
public static class ZkStagingHostedServiceRegistration
{
    public static bool Register(IServiceCollection services, AttendanceSourceOptions options)
    {
        if (!options.UsesStaging)
        {
            return false;
        }

        services.AddSingleton<ZkStagingSchemaValidationHostedService>();
        services.AddSingleton<IZkStagingSchemaReadiness>(provider =>
            provider.GetRequiredService<ZkStagingSchemaValidationHostedService>());
        services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<ZkStagingSchemaValidationHostedService>());

        if (!options.StagingProcessorEnabled)
        {
            return false;
        }

        services.AddHostedService<ZkStagingProcessingBackgroundService>();
        return true;
    }
}
