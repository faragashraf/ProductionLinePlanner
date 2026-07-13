using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

public sealed class WorkerInitialSyncService(
    AppDbContext dbContext,
    IAttendanceEmployeeReader attendanceEmployeeReader,
    IAuditEngine auditEngine) : IWorkerInitialSyncService
{
    public async Task<Result<WorkerInitialSyncResultDto>> SyncWorkersAsync(
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<WorkerInitialSyncResultDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        var startedAtUtc = DateTime.UtcNow;
        var completedAtUtc = startedAtUtc;

        Result<AttendanceEmployeeRecord[]> sourceResult;
        try
        {
            sourceResult = await attendanceEmployeeReader.GetAllAsync(cancellationToken);
        }
        catch
        {
            return Result<WorkerInitialSyncResultDto>.Failure(new Error(
                "AttendanceSourceError",
                "Unable to read worker data from attendance source."));
        }

        if (sourceResult.IsFailure)
        {
            return Result<WorkerInitialSyncResultDto>.Failure(sourceResult.Error!);
        }

        var sourceRows = sourceResult.Value ?? [];
        var sourceCount = sourceRows.Length;

        // Safe for v1: no department scope override detected in current product implementation.
        var localWorkers = await dbContext.Workers.ToListAsync(cancellationToken);
        var nonUniqueAttendanceUserIds = localWorkers
            .Where(x => !string.IsNullOrWhiteSpace(x.AttendanceUserId))
            .Select(x => x.AttendanceUserId!.Trim())
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nonUniqueBadges = localWorkers
            .Where(x => !string.IsNullOrWhiteSpace(x.BadgeNumber))
            .Select(x => x.BadgeNumber!.Trim())
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var localByAttendanceUserId = localWorkers
            .Where(x => !string.IsNullOrWhiteSpace(x.AttendanceUserId))
            .GroupBy(x => x.AttendanceUserId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);
        var localByBadge = localWorkers
            .Where(x => !string.IsNullOrWhiteSpace(x.BadgeNumber))
            .GroupBy(x => x.BadgeNumber!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);
        var localByEmployeeCode = localWorkers.ToDictionary(x => x.EmployeeCode, x => x, StringComparer.OrdinalIgnoreCase);

        var matchedWorkerIds = new HashSet<Guid>();
        var seenAttendanceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenBadgeFallbacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var createdCount = 0;
        var updatedCount = 0;
        var unchangedCount = 0;
        var warningCount = 0;

        var now = DateTime.UtcNow;

        foreach (var sourceRow in sourceRows)
        {
            var sourceAttendanceId = NormalizeString(sourceRow.AttendanceUserId);
            var sourceBadge = NormalizeString(sourceRow.BadgeNumber);
            var sourceName = NormalizeString(sourceRow.Name);
            var sourceDepartmentId = sourceRow.DepartmentId;

            var hasAttendanceId = !string.IsNullOrWhiteSpace(sourceAttendanceId);
            var hasBadge = !string.IsNullOrWhiteSpace(sourceBadge);
            var hasUsableName = IsUsableName(sourceName);

            if (hasAttendanceId)
            {
                if (!seenAttendanceIds.Add(sourceAttendanceId))
                {
                    warningCount++;
                    continue;
                }
                if (nonUniqueAttendanceUserIds.Contains(sourceAttendanceId))
                {
                    warningCount++;
                    continue;
                }

                if (localByAttendanceUserId.TryGetValue(sourceAttendanceId, out var localWorker))
                {
                    ApplySourceToWorker(localWorker, now, sourceAttendanceId, hasUsableName ? sourceName : null, sourceBadge, sourceDepartmentId,
                        out var changedInData);
                    matchedWorkerIds.Add(localWorker.Id);

                    if (changedInData)
                    {
                        updatedCount++;
                    }
                    else
                    {
                        unchangedCount++;
                    }

                    if (!hasUsableName)
                    {
                        warningCount++;
                    }

                    continue;
                }

                var employeeCode = sourceBadge ?? sourceAttendanceId;
                if (string.IsNullOrWhiteSpace(employeeCode))
                {
                    warningCount++;
                    continue;
                }

                if (localByEmployeeCode.ContainsKey(employeeCode))
                {
                    warningCount++;
                    continue;
                }

                if (!hasUsableName)
                {
                    warningCount++;
                }

                var newWorker = CreateWorker(
                    employeeCode,
                    hasUsableName ? sourceName : employeeCode,
                    sourceAttendanceId,
                    sourceBadge,
                    sourceDepartmentId,
                    now);

                dbContext.Workers.Add(newWorker);
                localByAttendanceUserId[sourceAttendanceId] = newWorker;
                localByEmployeeCode[employeeCode] = newWorker;
                if (hasBadge)
                {
                    localByBadge[sourceBadge!] = newWorker;
                }

                createdCount++;
                matchedWorkerIds.Add(newWorker.Id);
                continue;
            }

            if (hasBadge)
            {
                if (!seenBadgeFallbacks.Add(sourceBadge))
                {
                    warningCount++;
                    continue;
                }
                if (nonUniqueBadges.Contains(sourceBadge))
                {
                    warningCount++;
                    continue;
                }

                if (localByBadge.TryGetValue(sourceBadge, out var localWorker))
                {
                    ApplySourceToWorker(localWorker, now, null, hasUsableName ? sourceName : null, sourceBadge, sourceDepartmentId,
                        out var changedInData);
                    matchedWorkerIds.Add(localWorker.Id);

                    if (changedInData)
                    {
                        updatedCount++;
                    }
                    else
                    {
                        unchangedCount++;
                    }

                    if (!hasUsableName)
                    {
                        warningCount++;
                    }

                    continue;
                }

                if (localByEmployeeCode.ContainsKey(sourceBadge))
                {
                    warningCount++;
                    continue;
                }

                var employeeCode = sourceBadge;
                if (!hasUsableName)
                {
                    warningCount++;
                }

                var newWorker = CreateWorker(
                    employeeCode,
                    hasUsableName ? sourceName : employeeCode,
                    null,
                    sourceBadge,
                    sourceDepartmentId,
                    now);

                dbContext.Workers.Add(newWorker);
                localByBadge[sourceBadge] = newWorker;
                localByEmployeeCode[employeeCode] = newWorker;
                createdCount++;
                matchedWorkerIds.Add(newWorker.Id);
                continue;
            }

            // No key to safely match.
            warningCount++;
        }

        var missingFromSourceCount = localWorkers.Count(x => !matchedWorkerIds.Contains(x.Id));
        completedAtUtc = DateTime.UtcNow;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await RecordAuditAsync(actorUserId, requestMeta, new WorkerInitialSyncResultDto
            {
                SourceCount = sourceCount,
                CreatedCount = createdCount,
                UpdatedCount = updatedCount,
                UnchangedCount = unchangedCount,
                MissingFromSourceCount = missingFromSourceCount,
                WarningCount = warningCount,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc
            }, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<WorkerInitialSyncResultDto>.Failure(new Error(
                "WorkerInitialSyncFailed",
                "Unable to persist initial worker sync results."));
        }

        return Result<WorkerInitialSyncResultDto>.Success(new WorkerInitialSyncResultDto
        {
            SourceCount = sourceCount,
            CreatedCount = createdCount,
            UpdatedCount = updatedCount,
            UnchangedCount = unchangedCount,
            MissingFromSourceCount = missingFromSourceCount,
            WarningCount = warningCount,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc
        });
    }

    private static bool IsUsableName(string? sourceName) =>
        !string.IsNullOrWhiteSpace(sourceName) &&
        sourceName.Trim().All(char.IsDigit) is false;

    private static string? NormalizeString(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static Worker CreateWorker(
        string employeeCode,
        string fullName,
        string? attendanceUserId,
        string? badgeNumber,
        int? attendanceDepartmentId,
        DateTime now)
    {
        return new Worker(
            id: Guid.NewGuid(),
            employeeCode: employeeCode,
            fullName: fullName,
            attendanceUserId: attendanceUserId,
            badgeNumber: badgeNumber,
            isActive: true,
            attendanceDepartmentId: attendanceDepartmentId,
            lastExternalSyncAt: now,
            createdAtUtc: now,
            phone: null,
            employmentStatus: EmploymentStatus.Active);
    }

    private static void ApplySourceToWorker(
        Worker worker,
        DateTime now,
        string? sourceAttendanceUserId,
        string? sourceName,
        string? sourceBadge,
        int? sourceDepartmentId,
        out bool changedInData)
    {
        var normalizedAttendanceUserId = NormalizeString(sourceAttendanceUserId);
        var normalizedName = NormalizeString(sourceName);
        var normalizedBadge = NormalizeString(sourceBadge);
        var validName = !string.IsNullOrWhiteSpace(normalizedName) && !normalizedName.All(char.IsDigit);

        var hasDataChanges =
            (worker.AttendanceDepartmentId != sourceDepartmentId)
            || (!string.IsNullOrWhiteSpace(normalizedBadge) && !string.Equals(worker.BadgeNumber, normalizedBadge, StringComparison.Ordinal))
            || (validName && !string.Equals(worker.FullName, normalizedName, StringComparison.Ordinal));

        worker.ApplyAttendanceSync(now, normalizedAttendanceUserId, validName ? normalizedName : null, normalizedBadge, sourceDepartmentId);
        changedInData = hasDataChanges;
    }

    private async Task RecordAuditAsync(
        Guid actorUserId,
        string? requestMeta,
        WorkerInitialSyncResultDto result,
        CancellationToken cancellationToken)
    {
        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.WorkerInitialSync,
            nameof(Worker),
            nameof(Worker),
            before: null,
            after: result,
            requestMeta: requestMeta,
            cancellationToken: cancellationToken);
    }
}
