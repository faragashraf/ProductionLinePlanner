using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Infrastructure.Attendance;
using ProductionLinePlanner.Infrastructure.Attendance.Services;

namespace ProductionLinePlanner.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var appConnectionString = configuration.GetConnectionString("AppDatabase")
            ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:AppDatabase' is required.");
        var attendanceSourceSection = configuration.GetSection(AttendanceSourceOptions.SectionName);
        var attendanceConnectionString = attendanceSourceSection["ConnectionString"]
            ?? configuration.GetConnectionString("AttendanceDatabase")
            ?? string.Empty;

        var sourceName = attendanceSourceSection["SourceName"]?.Trim();
        var dayStartTime = attendanceSourceSection["DayStartTime"];
        var lateThresholdMinutes = attendanceSourceSection["LateThresholdMinutes"];
        var userInfoTable = attendanceSourceSection["UserInfoTable"]?.Trim();
        var checkInOutTable = attendanceSourceSection["CheckInOutTable"]?.Trim();
        var departmentsTable = attendanceSourceSection["DepartmentsTable"]?.Trim();

        var attendanceSourceOptions = new AttendanceSourceOptions
        {
            ConnectionString = attendanceConnectionString,
            SourceName = string.IsNullOrWhiteSpace(sourceName) ? "AttendanceSync" : sourceName,
            DayStartTime = TimeSpan.TryParse(dayStartTime, out var parsedDayStart) ? parsedDayStart : new TimeSpan(8, 0, 0),
            LateThresholdMinutes = int.TryParse(lateThresholdMinutes, out var parsedLateThreshold) ? parsedLateThreshold : 15,
            UserInfoTable = string.IsNullOrWhiteSpace(userInfoTable) ? "USERINFO" : userInfoTable,
            CheckInOutTable = string.IsNullOrWhiteSpace(checkInOutTable) ? "CHECKINOUT" : checkInOutTable,
            DepartmentsTable = string.IsNullOrWhiteSpace(departmentsTable) ? "DEPARTMENTS" : departmentsTable
        };

        services.AddSingleton(Options.Create(attendanceSourceOptions));

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlServer(appConnectionString);
        });

        services.AddDbContext<AttendanceDbContext>(options =>
        {
            if (!string.IsNullOrWhiteSpace(attendanceConnectionString))
            {
                options.UseSqlServer(attendanceConnectionString);
            }

            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        services.AddScoped<IAttendanceReadService, AttendanceSyncService>();
        services.AddScoped<IAttendanceSyncService, AttendanceSyncService>();

        return services;
    }
}
