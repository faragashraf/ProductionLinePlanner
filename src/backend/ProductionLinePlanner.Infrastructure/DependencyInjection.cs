using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Workers;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Infrastructure.Attendance;
using ProductionLinePlanner.Infrastructure.Attendance.Services;
using ProductionLinePlanner.Infrastructure.Workers;
using ProductionLinePlanner.Infrastructure.Authorization;
using ProductionLinePlanner.Infrastructure.Bootstrap;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Importing;
using ProductionLinePlanner.Infrastructure.Time;
using ProductionLinePlanner.Application.Realtime;
using ProductionLinePlanner.Infrastructure.Realtime;
using ProductionLinePlanner.Application.Notifications;
using ProductionLinePlanner.Infrastructure.Notifications;

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
        var sourceMode = attendanceSourceSection["Mode"]?.Trim();
        var dayStartTime = attendanceSourceSection["DayStartTime"];
        var lateThresholdMinutes = attendanceSourceSection["LateThresholdMinutes"];
        var userInfoTable = attendanceSourceSection["UserInfoTable"]?.Trim();
        var checkInOutTable = attendanceSourceSection["CheckInOutTable"]?.Trim();
        var departmentsTable = attendanceSourceSection["DepartmentsTable"]?.Trim();
        var syncReadCommandTimeoutSeconds = attendanceSourceSection["SyncReadCommandTimeoutSeconds"];
        var syncReadTimeoutSeconds = attendanceSourceSection["SyncReadTimeoutSeconds"];
        var stagingBatchSize = attendanceSourceSection["StagingBatchSize"];
        var processingLeaseMinutes = attendanceSourceSection["ProcessingLeaseMinutes"];
        var maxProcessingAttempts = attendanceSourceSection["MaxProcessingAttempts"];
        var stagingProcessorEnabled = attendanceSourceSection["StagingProcessorEnabled"];
        var stagingProcessorIntervalSeconds = attendanceSourceSection["StagingProcessorIntervalSeconds"];
        var maxPendingProductionDates = attendanceSourceSection["MaxPendingProductionDates"];

        if (!string.IsNullOrWhiteSpace(sourceMode) &&
            !string.Equals(sourceMode, AttendanceSourceOptions.DirectMode, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sourceMode, AttendanceSourceOptions.StagingMode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("AttendanceSource:Mode must be 'Direct' or 'Staging'.");
        }

        var attendanceSourceOptions = new AttendanceSourceOptions
        {
            ConnectionString = attendanceConnectionString,
            Mode = string.IsNullOrWhiteSpace(sourceMode) ? AttendanceSourceOptions.DirectMode : sourceMode,
            SourceName = string.IsNullOrWhiteSpace(sourceName) ? "AttendanceSync" : sourceName,
            DayStartTime = TimeSpan.TryParse(dayStartTime, out var parsedDayStart) ? parsedDayStart : new TimeSpan(8, 0, 0),
            LateThresholdMinutes = int.TryParse(lateThresholdMinutes, out var parsedLateThreshold) ? parsedLateThreshold : 15,
            UserInfoTable = string.IsNullOrWhiteSpace(userInfoTable) ? "USERINFO" : userInfoTable,
            CheckInOutTable = string.IsNullOrWhiteSpace(checkInOutTable) ? "CHECKINOUT" : checkInOutTable,
            DepartmentsTable = string.IsNullOrWhiteSpace(departmentsTable) ? "DEPARTMENTS" : departmentsTable,
            SyncReadCommandTimeoutSeconds = int.TryParse(syncReadCommandTimeoutSeconds, out var parsedSyncReadCommandTimeout) ? Math.Max(1, parsedSyncReadCommandTimeout) : 30,
            SyncReadTimeoutSeconds = int.TryParse(syncReadTimeoutSeconds, out var parsedSyncReadTimeout) ? Math.Max(1, parsedSyncReadTimeout) : 35,
            StagingBatchSize = int.TryParse(stagingBatchSize, out var parsedStagingBatchSize) ? Math.Clamp(parsedStagingBatchSize, 1, 10000) : 2000,
            ProcessingLeaseMinutes = int.TryParse(processingLeaseMinutes, out var parsedProcessingLeaseMinutes) ? Math.Clamp(parsedProcessingLeaseMinutes, 1, 120) : 15,
            MaxProcessingAttempts = int.TryParse(maxProcessingAttempts, out var parsedMaxProcessingAttempts) ? Math.Clamp(parsedMaxProcessingAttempts, 1, 100) : 5,
            StagingProcessorEnabled = !bool.TryParse(stagingProcessorEnabled, out var parsedStagingProcessorEnabled) || parsedStagingProcessorEnabled,
            StagingProcessorIntervalSeconds = int.TryParse(stagingProcessorIntervalSeconds, out var parsedProcessorInterval) ? Math.Clamp(parsedProcessorInterval, 15, 3600) : 60,
            MaxPendingProductionDates = int.TryParse(maxPendingProductionDates, out var parsedMaximumDates) ? Math.Clamp(parsedMaximumDates, 1, 31) : 3
        };

        services.AddSingleton(Options.Create(attendanceSourceOptions));
        services.AddSingleton<ICairoTimeZoneProvider, CairoTimeZoneProvider>();

        services.AddScoped<IManufacturingDataChangePublisher, NoopManufacturingDataChangePublisher>();
        services.AddScoped<IManufacturingRealtimeCorrelationContext, NoopManufacturingRealtimeCorrelationContext>();
        services.AddScoped<ManufacturingDataChangeTransactionCoordinator>();
        services.AddScoped<ManufacturingDataChangeSaveChangesInterceptor>();
        services.AddScoped<ManufacturingDataChangeTransactionInterceptor>();
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(appConnectionString);
            options.AddInterceptors(
                serviceProvider.GetRequiredService<ManufacturingDataChangeSaveChangesInterceptor>(),
                serviceProvider.GetRequiredService<ManufacturingDataChangeTransactionInterceptor>());
        });

        services.AddDbContext<AttendanceDbContext>(options =>
        {
            options.UseSqlServer(attendanceConnectionString);
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        services.AddScoped<ZkTimeDirectAttendanceSource>();
        services.AddSingleton<IZkStagingSchemaValidator>(new ZkStagingSchemaValidator(appConnectionString));
        services.AddScoped<ZkTimeStagingSource>(provider => new ZkTimeStagingSource(
            appConnectionString,
            provider.GetRequiredService<IOptions<AttendanceSourceOptions>>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ZkTimeStagingSource>>()));
        services.AddScoped<IWorkerIdentitySource>(provider => attendanceSourceOptions.UsesStaging
            ? provider.GetRequiredService<ZkTimeStagingSource>()
            : provider.GetRequiredService<AttendanceDirectoryService>());
        services.AddScoped<IAttendanceSource>(provider => attendanceSourceOptions.UsesStaging
            ? provider.GetRequiredService<ZkTimeStagingSource>()
            : provider.GetRequiredService<ZkTimeDirectAttendanceSource>());
        services.AddScoped<IZkStagingBacklogReader>(provider => provider.GetRequiredService<ZkTimeStagingSource>());
        services.AddScoped<AttendanceSyncService>();
        services.AddScoped<IAttendanceReadService>(serviceProvider => serviceProvider.GetRequiredService<AttendanceSyncService>());
        services.AddScoped<IAttendanceSyncRunner>(serviceProvider => serviceProvider.GetRequiredService<AttendanceSyncService>());
        services.AddSingleton<IAttendanceSyncService, AttendanceSyncCoordinator>();
        services.AddScoped<IAttendanceEngine, AttendanceEngine>();
        services.AddScoped<IAssignmentEngine, AssignmentEngine>();
        services.AddScoped<IAttendanceWorkforceEngine, AttendanceWorkforceEngine>();
        services.AddScoped<IAssignmentRecommendationEngine, AssignmentRecommendationEngine>();
        services.AddScoped<IReadinessEngine, ReadinessEngine>();
        services.AddScoped<IManufacturingCommandCenterEngine, ManufacturingCommandCenterEngine>();
        services.AddScoped<INotificationEngine, NotificationEngine>();
        services.AddSingleton<INotificationEventCatalog, CodeNotificationEventCatalog>();
        services.AddSingleton<INotificationTemplateResolver, NotificationTemplateResolver>();
        services.AddScoped<INotificationRecipientResolver, NotificationRecipientResolver>();
        services.AddScoped<INotificationPolicyEngine, NotificationPolicyEngine>();
        services.AddScoped<IAssignmentNotificationDispatcher, AssignmentNotificationDispatcher>();
        services.AddScoped<INotificationPolicyCatalogReconciler, NotificationPolicyCatalogReconciler>();
        services.AddScoped<INotificationPolicyAdminService, NotificationPolicyAdminService>();
        services.AddScoped<ICapabilityGroupResolver, CapabilityGroupResolver>();
        services.AddScoped<INotificationPublisher, NotificationPublisher>();
        services.AddScoped<IAuditEngine, AuditEngine>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IIamDelegationPolicy, IamDelegationPolicy>();
        services.AddScoped<IIamAuthorizationService, IamAuthorizationService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IRolePermissionSeedService, PermissionSeedService>();

        services.AddScoped<AttendanceDirectoryService>();
        services.AddScoped<IAttendanceEmployeeReader>(provider => provider.GetRequiredService<AttendanceDirectoryService>());
        services.AddScoped<IAttendanceWorkerPhotoReader>(provider => provider.GetRequiredService<AttendanceDirectoryService>());
        services.AddSingleton<IWorkerPhotoStorage, LocalWorkerPhotoStorage>();
        services.AddScoped<IWorkerPhotoService, WorkerPhotoService>();
        services.AddScoped<IAttendanceDepartmentReader>(provider => provider.GetRequiredService<AttendanceDirectoryService>());

        services.AddWorkerSyncFoundation();
        services.AddScoped<IEmployeeMasterDataService, EmployeeMasterDataService>();
        services.AddScoped<IDepartmentAdministrationService, DepartmentAdministrationService>();
        services.AddScoped<IDepartmentCatalogService, DepartmentCatalogService>();
        services.AddScoped<IProductionStageCatalogService, ProductionStageCatalogService>();
        services.AddScoped<IStageDependencyInspector, StageDependencyInspector>();
        services.AddScoped<IProductModelService, ProductModelService>();
        services.AddScoped<IWorkerCompensationService, WorkerCompensationService>();
        services.AddScoped<IProductionCostRecordingService, ProductionCostRecordingService>();
        services.AddScoped<IProductionQuantitiesReportService, ProductionQuantitiesReportService>();
        services.AddScoped<IProductionFinancialReportService, ProductionFinancialReportService>();
        services.AddScoped<IProductionReadinessEngine, ProductionReadinessEngine>();
        services.AddScoped<ILineStaffingEngine, LineStaffingEngine>();
        services.AddScoped<IImportNormalizationService, ImportNormalizationService>();
        services.AddScoped<IPilotMasterDataBootstrapService, PilotMasterDataBootstrapService>();
        services.AddScoped<IPilotMasterDataResetService, PilotMasterDataResetService>();
        services.AddScoped<IRealDataIntakeService, RealDataIntakeService>();

        return services;
    }

    private static IServiceCollection AddWorkerSyncFoundation(this IServiceCollection services)
    {
        services.AddScoped<IWorkerSyncPolicy, WorkerSyncPolicy>();
        services.AddScoped<IAuthoritativeWorkerSnapshotValidator, AuthoritativeWorkerSnapshotValidator>();
        services.AddScoped<IWorkerInitialSyncService, WorkerInitialSyncService>();
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
