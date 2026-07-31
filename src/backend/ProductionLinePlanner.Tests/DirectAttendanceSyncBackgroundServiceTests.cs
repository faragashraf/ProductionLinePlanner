using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Api.HostedServices;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Infrastructure.Attendance;

namespace ProductionLinePlanner.Tests;

public sealed class DirectAttendanceSyncBackgroundServiceTests
{
    [Fact]
    public void Direct_enabled_registers_processor_while_disabled_or_staging_modes_do_not()
    {
        var enabled = new ServiceCollection();
        var enabledRegistered = DirectAttendanceSyncHostedServiceRegistration.Register(enabled, new AttendanceSourceOptions
        {
            Mode = AttendanceSourceOptions.DirectMode,
            DirectSyncEnabled = true
        });
        var disabled = new ServiceCollection();
        var disabledRegistered = DirectAttendanceSyncHostedServiceRegistration.Register(disabled, new AttendanceSourceOptions
        {
            Mode = AttendanceSourceOptions.DirectMode,
            DirectSyncEnabled = false
        });
        var staging = new ServiceCollection();
        var stagingRegistered = DirectAttendanceSyncHostedServiceRegistration.Register(staging, new AttendanceSourceOptions
        {
            Mode = AttendanceSourceOptions.StagingMode,
            DirectSyncEnabled = true
        });

        Assert.True(enabledRegistered);
        Assert.False(disabledRegistered);
        Assert.False(stagingRegistered);
        Assert.Contains(enabled, descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                                               descriptor.ImplementationType == typeof(DirectAttendanceSyncBackgroundService));
        Assert.DoesNotContain(disabled, descriptor => descriptor.ImplementationType == typeof(DirectAttendanceSyncBackgroundService));
        Assert.DoesNotContain(staging, descriptor => descriptor.ImplementationType == typeof(DirectAttendanceSyncBackgroundService));
    }

    [Fact]
    public async Task Cycle_synchronizes_the_current_operational_day()
    {
        var sync = new RecordingAttendanceSync();
        var logger = new RecordingLogger<DirectAttendanceSyncBackgroundService>();
        var service = CreateService(sync, logger);

        await service.ProcessCycleAsync(CancellationToken.None);

        Assert.Equal(1, sync.TodayCallCount);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Failure_is_logged_but_an_in_progress_manual_sync_is_not_reported_as_an_outage()
    {
        var failedSync = new RecordingAttendanceSync("AttendanceSourceTimeout");
        var failedLogger = new RecordingLogger<DirectAttendanceSyncBackgroundService>();
        await CreateService(failedSync, failedLogger).ProcessCycleAsync(CancellationToken.None);

        var busySync = new RecordingAttendanceSync("AttendanceSyncInProgress");
        var busyLogger = new RecordingLogger<DirectAttendanceSyncBackgroundService>();
        await CreateService(busySync, busyLogger).ProcessCycleAsync(CancellationToken.None);

        Assert.Contains(failedLogger.Entries, entry => entry.Level == LogLevel.Warning &&
                                                      entry.Message.Contains("AttendanceSourceTimeout", StringComparison.Ordinal));
        Assert.DoesNotContain(busyLogger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    private static DirectAttendanceSyncBackgroundService CreateService(
        IAttendanceSyncService attendanceSync,
        RecordingLogger<DirectAttendanceSyncBackgroundService> logger) =>
        new(
            attendanceSync,
            Options.Create(new AttendanceSourceOptions
            {
                Mode = AttendanceSourceOptions.DirectMode,
                DirectSyncIntervalSeconds = 60
            }),
            logger);

    private sealed class RecordingAttendanceSync(string? errorCode = null) : IAttendanceSyncService
    {
        public int TodayCallCount { get; private set; }

        public Task<Result<AttendanceSyncResultDto>> SyncTodayAsync(CancellationToken cancellationToken = default)
        {
            TodayCallCount++;
            return Task.FromResult(errorCode is null
                ? Result<AttendanceSyncResultDto>.Success(new AttendanceSyncResultDto())
                : Result<AttendanceSyncResultDto>.Failure(new Error(errorCode, "Synthetic failure")));
        }

        public Task<Result<AttendanceSyncResultDto>> SyncForProductionDateAsync(
            DateOnly productionDate,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
