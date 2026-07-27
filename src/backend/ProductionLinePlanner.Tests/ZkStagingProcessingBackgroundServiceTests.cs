using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Api.HostedServices;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Infrastructure.Attendance;
using ProductionLinePlanner.Infrastructure.Attendance.Services;

namespace ProductionLinePlanner.Tests;

public sealed class ZkStagingProcessingBackgroundServiceTests
{
    [Fact]
    public void Staging_enabled_registers_processor_and_disabled_does_not()
    {
        var enabled = new ServiceCollection();
        var registered = ZkStagingHostedServiceRegistration.Register(enabled, new AttendanceSourceOptions
        {
            Mode = AttendanceSourceOptions.StagingMode,
            StagingProcessorEnabled = true
        });

        var disabled = new ServiceCollection();
        var disabledRegistered = ZkStagingHostedServiceRegistration.Register(disabled, new AttendanceSourceOptions
        {
            Mode = AttendanceSourceOptions.StagingMode,
            StagingProcessorEnabled = false
        });

        Assert.True(registered);
        Assert.False(disabledRegistered);
        Assert.Contains(enabled, descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                                               descriptor.ImplementationType == typeof(ZkStagingProcessingBackgroundService));
        Assert.DoesNotContain(disabled, descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                                                    descriptor.ImplementationType == typeof(ZkStagingProcessingBackgroundService));
    }

    [Fact]
    public async Task Cycle_reads_backlog_and_forwards_each_pending_date_to_attendance_sync()
    {
        var reader = new FakeBacklogReader(Result<DateOnly[]>.Success([new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 16)]));
        var sync = new RecordingAttendanceSync();
        var logger = new RecordingLogger<ZkStagingProcessingBackgroundService>();
        await using var provider = CreateProvider(reader);
        var service = CreateService(provider, sync, logger);

        await service.ProcessCycleAsync(CancellationToken.None);

        Assert.Equal(1, reader.CallCount);
        Assert.Equal([new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 16)], sync.Dates);
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("pendingDateCount=2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Backlog_failure_is_logged_and_does_not_run_attendance_sync()
    {
        var reader = new FakeBacklogReader(Result<DateOnly[]>.Failure(new Error("StagingSourceError", "Unavailable")));
        var sync = new RecordingAttendanceSync();
        var logger = new RecordingLogger<ZkStagingProcessingBackgroundService>();
        await using var provider = CreateProvider(reader);
        var service = CreateService(provider, sync, logger);

        await service.ProcessCycleAsync(CancellationToken.None);

        Assert.Equal(1, reader.CallCount);
        Assert.Empty(sync.Dates);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("backlog could not be inspected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Empty_backlog_does_not_run_today_sync_without_a_staged_date()
    {
        var reader = new FakeBacklogReader(Result<DateOnly[]>.Success([]));
        var sync = new RecordingAttendanceSync();
        var logger = new RecordingLogger<ZkStagingProcessingBackgroundService>();
        await using var provider = CreateProvider(reader);
        var service = CreateService(provider, sync, logger);

        await service.ProcessCycleAsync(CancellationToken.None);

        Assert.Equal(1, reader.CallCount);
        Assert.Empty(sync.Dates);
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("pendingDateCount=0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Exception_in_one_cycle_does_not_stop_the_background_service()
    {
        var reader = new ThrowThenSucceedBacklogReader();
        var sync = new RecordingAttendanceSync();
        var logger = new RecordingLogger<ZkStagingProcessingBackgroundService>();
        await using var provider = CreateProvider(reader);
        var service = CreateService(provider, sync, logger, intervalSeconds: 1);

        await service.StartAsync(CancellationToken.None);
        await reader.SecondCall.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.True(reader.CallCount >= 2);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("Unexpected failure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_interval_uses_safe_fallback_and_logs_the_decision()
    {
        var reader = new FakeBacklogReader(Result<DateOnly[]>.Success([]));
        var sync = new RecordingAttendanceSync();
        var logger = new RecordingLogger<ZkStagingProcessingBackgroundService>();
        await using var provider = CreateProvider(reader);
        var service = CreateService(provider, sync, logger, intervalSeconds: 0);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await service.StopAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("invalid interval", StringComparison.OrdinalIgnoreCase));
    }

    private static ServiceProvider CreateProvider(IZkStagingBacklogReader reader)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => reader);
        return services.BuildServiceProvider();
    }

    private static ZkStagingProcessingBackgroundService CreateService(
        ServiceProvider provider,
        IAttendanceSyncService attendanceSync,
        RecordingLogger<ZkStagingProcessingBackgroundService> logger,
        int intervalSeconds = 60) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            attendanceSync,
            Options.Create(new AttendanceSourceOptions
            {
                Mode = AttendanceSourceOptions.StagingMode,
                StagingProcessorIntervalSeconds = intervalSeconds,
                MaxPendingProductionDates = 3
            }),
            new ReadySchemaReadiness(),
            logger);

    private sealed class ReadySchemaReadiness : IZkStagingSchemaReadiness
    {
        public Task WaitUntilReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeBacklogReader(Result<DateOnly[]> result) : IZkStagingBacklogReader
    {
        public int CallCount { get; private set; }

        public Task<Result<DateOnly[]>> GetPendingProductionDatesAsync(TimeSpan dayStartTime, int maximumDates, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowThenSucceedBacklogReader : IZkStagingBacklogReader
    {
        public int CallCount { get; private set; }
        public TaskCompletionSource SecondCall { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Result<DateOnly[]>> GetPendingProductionDatesAsync(TimeSpan dayStartTime, int maximumDates, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount == 1)
            {
                throw new InvalidOperationException("Synthetic backlog failure");
            }

            SecondCall.TrySetResult();
            return Task.FromResult(Result<DateOnly[]>.Success([]));
        }
    }

    private sealed class RecordingAttendanceSync : IAttendanceSyncService
    {
        public List<DateOnly> Dates { get; } = [];

        public Task<Result<AttendanceSyncResultDto>> SyncTodayAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<AttendanceSyncResultDto>.Success(new AttendanceSyncResultDto()));

        public Task<Result<AttendanceSyncResultDto>> SyncForProductionDateAsync(DateOnly productionDate, CancellationToken cancellationToken = default)
        {
            Dates.Add(productionDate);
            return Task.FromResult(Result<AttendanceSyncResultDto>.Success(new AttendanceSyncResultDto()));
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
