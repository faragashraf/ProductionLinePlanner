using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Infrastructure.Attendance;
using ProductionLinePlanner.Infrastructure.Attendance.Services;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Tests.TestInfrastructure;

namespace ProductionLinePlanner.Tests;

public sealed class AttendanceSyncReliabilityTests
{
    [Fact]
    public async Task Duplicate_requests_for_the_same_date_do_not_start_duplicate_source_reads()
    {
        var runner = new BlockingRunner();
        var coordinator = CreateCoordinator(runner);
        var date = new DateOnly(2026, 7, 16);

        var first = coordinator.SyncForProductionDateAsync(date);
        await runner.Started.Task;

        var duplicate = await coordinator.SyncForProductionDateAsync(date);

        Assert.True(duplicate.IsFailure);
        Assert.Equal("AttendanceSyncInProgress", duplicate.Error?.Code);
        Assert.Equal(1, runner.CallCount);

        runner.Complete();
        Assert.True((await first).IsSuccess);
    }

    [Fact]
    public async Task Duplicate_request_can_start_after_the_completed_operation_is_removed_during_contention_lookup()
    {
        var runner = new BlockingRunner();
        Task<Result<AttendanceSyncResultDto>>? first = null;
        var coordinator = CreateCoordinator(runner, async () =>
        {
            runner.Complete();
            await first!;
        });
        var date = new DateOnly(2026, 7, 16);

        first = coordinator.SyncForProductionDateAsync(date);
        await runner.Started.Task;

        var duplicate = await coordinator.SyncForProductionDateAsync(date);

        Assert.True(duplicate.IsSuccess);
        Assert.Equal(2, runner.CallCount);
    }

    [Fact]
    public async Task Client_cancellation_ends_only_the_callers_wait_and_not_the_active_sync()
    {
        var runner = new BlockingRunner();
        var coordinator = CreateCoordinator(runner);
        using var cancellation = new CancellationTokenSource();

        var waitingCaller = coordinator.SyncForProductionDateAsync(new DateOnly(2026, 7, 16), cancellation.Token);
        await runner.Started.Task;
        cancellation.Cancel();

        var cancelled = await waitingCaller;

        Assert.True(cancelled.IsFailure);
        Assert.Equal(AttendanceSyncFailureClassifier.ClientCancelled, cancelled.Error?.Code);
        Assert.Equal(1, runner.CallCount);
        Assert.False(runner.Completion.Task.IsCompleted);

        runner.Complete();
    }

    [Fact]
    public async Task Cancelled_direct_sync_preserves_previously_completed_attendance_rows()
    {
        var sourceOptions = Options.Create(new AttendanceSourceOptions());
        await using var appDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        await using var attendanceDb = new AttendanceDbContext(
            new DbContextOptionsBuilder<AttendanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options,
            sourceOptions);
        appDb.Workers.Add(new Worker(Guid.NewGuid(), "001", "Active", attendanceUserId: "1001"));
        await appDb.SaveChangesAsync();
        var service = new AttendanceSyncService(appDb, attendanceDb, sourceOptions, NullLogger<AttendanceSyncService>.Instance, TestCairoTimeZoneProvider.Instance);
        var date = new DateOnly(2026, 7, 16);

        Assert.True((await service.SyncForProductionDateAsync(date)).IsSuccess);
        var completedRecordCount = await appDb.AttendanceRecords.CountAsync();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await service.SyncForProductionDateAsync(date, cancellation.Token);

        Assert.True(cancelled.IsFailure);
        Assert.Equal(AttendanceSyncFailureClassifier.ClientCancelled, cancelled.Error?.Code);
        Assert.Equal(completedRecordCount, await appDb.AttendanceRecords.CountAsync());
    }

    [Fact]
    public async Task Core_sync_identity_query_does_not_select_userinfo_photo_blob()
    {
        var sourceOptions = Options.Create(new AttendanceSourceOptions());
        await using var sourceConnection = new SqliteConnection("Data Source=:memory:");
        await sourceConnection.OpenAsync();
        var commands = new CommandCaptureInterceptor();
        await using var attendanceDb = new AttendanceDbContext(
            new DbContextOptionsBuilder<AttendanceDbContext>()
                .UseSqlite(sourceConnection)
                .AddInterceptors(commands)
                .Options,
            sourceOptions);
        await attendanceDb.Database.ExecuteSqlRawAsync("CREATE TABLE USERINFO (USERID INTEGER NULL, BADGENUMBER TEXT NULL, Name TEXT NULL, DEFAULTDEPTID INTEGER NULL, PHOTO BLOB NULL);");
        await attendanceDb.Database.ExecuteSqlRawAsync("CREATE TABLE CHECKINOUT (USERID INTEGER NULL, CHECKTIME TEXT NULL, CHECKTYPE TEXT NULL);");
        await attendanceDb.Database.ExecuteSqlRawAsync("INSERT INTO USERINFO (USERID, BADGENUMBER, PHOTO) VALUES (1001, '001', X'010203');");

        await using var appDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var service = new AttendanceSyncService(appDb, attendanceDb, sourceOptions, NullLogger<AttendanceSyncService>.Instance, TestCairoTimeZoneProvider.Instance);

        var result = await service.SyncForProductionDateAsync(new DateOnly(2026, 7, 16));

        Assert.True(result.IsSuccess);
        var userInfoSelect = Assert.Single(commands.Commands, command => command.Contains("FROM \"USERINFO\"", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("PHOTO", userInfoSelect, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cancellation_and_source_timeout_are_classified_separately()
    {
        Assert.Equal(
            AttendanceSyncFailureClassifier.ClientCancelled,
            AttendanceSyncFailureClassifier.Classify(new OperationCanceledException(), requestTokenCancelled: true, internalTimeoutCancelled: false));
        Assert.Equal(
            AttendanceSyncFailureClassifier.InternalTimeout,
            AttendanceSyncFailureClassifier.Classify(new OperationCanceledException(), requestTokenCancelled: false, internalTimeoutCancelled: true));
        Assert.Equal(
            AttendanceSyncFailureClassifier.SourceTimeout,
            AttendanceSyncFailureClassifier.Classify(new TimeoutException(), requestTokenCancelled: false, internalTimeoutCancelled: false));
    }

    private static AttendanceSyncCoordinator CreateCoordinator(BlockingRunner runner, Func<Task>? afterFailedTryAddAsync = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<IAttendanceSyncRunner>(_ => runner);
        var provider = services.BuildServiceProvider();
        return afterFailedTryAddAsync is null
            ? new AttendanceSyncCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new AttendanceSourceOptions()),
                NullLogger<AttendanceSyncCoordinator>.Instance,
                TestCairoTimeZoneProvider.Instance)
            : new AttendanceSyncCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new AttendanceSourceOptions()),
                NullLogger<AttendanceSyncCoordinator>.Instance,
                TestCairoTimeZoneProvider.Instance,
                afterFailedTryAddAsync);
    }

    private sealed class BlockingRunner : IAttendanceSyncRunner
    {
        public int CallCount { get; private set; }
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Result<AttendanceSyncResultDto>> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Result<AttendanceSyncResultDto>> RunAsync(AttendanceSyncExecutionContext context, CancellationToken cancellationToken = default)
        {
            CallCount++;
            Started.TrySetResult(true);
            return Completion.Task;
        }

        public void Complete() => Completion.TrySetResult(Result<AttendanceSyncResultDto>.Success(new AttendanceSyncResultDto
        {
            CorrelationId = "test",
            TriggerType = "manual",
            SyncDateUtc = new DateTime(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc)
        }));
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
