using Microsoft.Extensions.DependencyInjection;
using ProductionLinePlanner.Infrastructure.Attendance;

namespace ProductionLinePlanner.Api.HostedServices;

public static class DirectAttendanceSyncHostedServiceRegistration
{
    public static bool Register(IServiceCollection services, AttendanceSourceOptions options)
    {
        if (options.UsesStaging || !options.DirectSyncEnabled)
        {
            return false;
        }

        services.AddHostedService<DirectAttendanceSyncBackgroundService>();
        return true;
    }
}
