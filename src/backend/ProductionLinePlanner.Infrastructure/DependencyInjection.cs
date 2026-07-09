using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Infrastructure.Attendance;
using ProductionLinePlanner.Infrastructure.Attendance.Services;
using ProductionLinePlanner.Infrastructure.BusinessEngines;

namespace ProductionLinePlanner.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var appConnectionString = configuration.GetConnectionString("AppDatabase")
            ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:AppDatabase' is required.");
        var attendanceConnectionString = ResolveAndValidateAttendanceConnectionString(configuration);
        var attendanceSourceSection = configuration.GetSection(AttendanceSourceOptions.SectionName);

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
            options.UseSqlServer(attendanceConnectionString);
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        services.AddScoped<IAttendanceReadService, AttendanceSyncService>();
        services.AddScoped<IAttendanceSyncService, AttendanceSyncService>();
        services.AddScoped<IAttendanceEngine, AttendanceEngine>();
        services.AddScoped<IAssignmentEngine, AssignmentEngine>();
        services.AddScoped<IReadinessEngine, ReadinessEngine>();
        services.AddScoped<INotificationEngine, NotificationEngine>();
        services.AddScoped<IAuditEngine, AuditEngine>();

        return services;
    }

    private static string ResolveAndValidateAttendanceConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AttendanceDatabase");

        if (string.IsNullOrWhiteSpace(connectionString) || IsPlaceholderValue(connectionString))
        {
            throw new InvalidOperationException(
                "Missing or placeholder value for 'ConnectionStrings:AttendanceDatabase'."
                + " Set a valid SQL Server connection string in user secrets or other configuration provider.");
        }

        try
        {
            _ = new SqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is FormatException)
        {
            throw new InvalidOperationException(
                "Invalid value for 'ConnectionStrings:AttendanceDatabase'."
                + " Ensure it is a valid SQL Server connection string.",
                ex);
        }

        return connectionString;
    }

    private static bool IsPlaceholderValue(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Contains("{{", StringComparison.Ordinal) ||
               trimmed.Contains("}}", StringComparison.Ordinal) ||
               trimmed.Contains("...", StringComparison.Ordinal) ||
               trimmed.Contains("TODO", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Contains("REPLACE", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Contains("<", StringComparison.Ordinal) ||
               trimmed.Contains(">", StringComparison.Ordinal) ||
               trimmed.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase);
    }
}
