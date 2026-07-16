using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Application.Workers;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

public sealed class WorkerInitialSyncService(
    AppDbContext dbContext,
    IAttendanceEmployeeReader attendanceEmployeeReader,
    IAttendanceWorkerPhotoReader attendanceWorkerPhotoReader,
    IWorkerPhotoCache workerPhotoCache,
    IAuditEngine auditEngine) : IWorkerInitialSyncService
{
    public async Task<Result<WorkerActiveServiceSyncPreviewDto>> PreviewActiveServiceSyncAsync(
        CancellationToken cancellationToken = default)
    {
        Result<AttendanceEmployeeRecord[]> sourceResult;
        try
        {
            sourceResult = await attendanceEmployeeReader.GetAllAsync(cancellationToken);
        }
        catch
        {
            return Result<WorkerActiveServiceSyncPreviewDto>.Failure(new Error(
                "AttendanceSourceError",
                "Unable to read worker data from attendance source."));
        }

        if (sourceResult.IsFailure)
        {
            return Result<WorkerActiveServiceSyncPreviewDto>.Failure(sourceResult.Error!);
        }

        var activeSourceRows = (sourceResult.Value ?? []).Where(x => x.IsActive).ToArray();
        var localWorkers = await dbContext.Workers.AsNoTracking().ToArrayAsync(cancellationToken);
        var workersInCurrentSource = localWorkers.Where(worker => IsRepresentedByActiveSource(worker, activeSourceRows)).ToArray();
        var inactiveOrExcluded = localWorkers.Except(workersInCurrentSource).ToArray();
        var sourceRowsWithoutLocalWorker = activeSourceRows.Count(source =>
            !localWorkers.Any(worker => IsSourceRowForWorker(source, worker)));

        return Result<WorkerActiveServiceSyncPreviewDto>.Success(new WorkerActiveServiceSyncPreviewDto
        {
            CurrentLocalWorkers = localWorkers.Length,
            ActiveOnServiceWorkersInZkTime = activeSourceRows.Length,
            WorkersToRemainActive = workersInCurrentSource.Count(x => x.IsActive && x.EmploymentStatus == EmploymentStatus.Active),
            WorkersToReactivate = workersInCurrentSource.Count(x => !x.IsActive || x.EmploymentStatus != EmploymentStatus.Active),
            WorkersToCreate = sourceRowsWithoutLocalWorker,
            WorkersToMarkInactiveOrExcluded = inactiveOrExcluded.Count(x => x.IsActive || x.EmploymentStatus != EmploymentStatus.LeftEmployment),
            WorkersAlreadyInactiveOrExcluded = inactiveOrExcluded.Count(x => !x.IsActive && x.EmploymentStatus == EmploymentStatus.LeftEmployment),
            WorkersSafelyRemovable = 0,
            WarningCount = 0
        });
    }

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

        Result<AttendanceWorkerPhotoRecord[]> sourcePhotosResult;
        try
        {
            sourcePhotosResult = await attendanceWorkerPhotoReader.GetAllCurrentPhotosAsync(cancellationToken);
        }
        catch
        {
            return Result<WorkerInitialSyncResultDto>.Failure(new Error(
                "AttendanceSourceError",
                "Unable to read worker photos from attendance source."));
        }

        if (sourcePhotosResult.IsFailure)
        {
            return Result<WorkerInitialSyncResultDto>.Failure(sourcePhotosResult.Error!);
        }

        // AttendanceDirectoryService limits this set to CurrentEmployeesImport membership.
        // Retaining the filter here makes the local projection safe with any future reader.
        var sourceRows = (sourceResult.Value ?? []).Where(x => x.IsActive).ToArray();
        var sourceCount = sourceRows.Length;
        var photosFoundCount = 0;
        var invalidOrUnsupportedPhotosCount = 0;
        var validPhotosByAttendanceUserId = new Dictionary<string, AttendanceWorkerPhotoRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourcePhoto in sourcePhotosResult.Value ?? [])
        {
            photosFoundCount++;
            if (WorkerPhotoFormat.TryGetContentType(sourcePhoto.Photo, out _))
            {
                validPhotosByAttendanceUserId[sourcePhoto.AttendanceUserId] = sourcePhoto;
            }
            else
            {
                invalidOrUnsupportedPhotosCount++;
            }
        }
        var photosSynchronizedCount = 0;
        var photosCreatedCount = 0;
        var photosUpdatedCount = 0;
        var photosUnchangedCount = 0;
        var workersWithoutPhotosCount = 0;

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
        var reactivatedCount = 0;

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
                        out var changedInData, out var reactivated);
                    var existingPhotoOutcome = await SynchronizePhotoReferenceAsync(localWorker, sourceAttendanceId, validPhotosByAttendanceUserId, now, cancellationToken);
                    photosSynchronizedCount += existingPhotoOutcome.Synchronized;
                    photosCreatedCount += existingPhotoOutcome.Created;
                    photosUpdatedCount += existingPhotoOutcome.Updated;
                    photosUnchangedCount += existingPhotoOutcome.Unchanged;
                    workersWithoutPhotosCount += existingPhotoOutcome.Missing;
                    matchedWorkerIds.Add(localWorker.Id);
                    if (reactivated) reactivatedCount++;

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
                var createdPhotoOutcome = await SynchronizePhotoReferenceAsync(newWorker, sourceAttendanceId, validPhotosByAttendanceUserId, now, cancellationToken);
                photosSynchronizedCount += createdPhotoOutcome.Synchronized;
                photosCreatedCount += createdPhotoOutcome.Created;
                photosUpdatedCount += createdPhotoOutcome.Updated;
                photosUnchangedCount += createdPhotoOutcome.Unchanged;
                workersWithoutPhotosCount += createdPhotoOutcome.Missing;

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
                        out var changedInData, out var reactivated);
                    var badgePhotoOutcome = await SynchronizePhotoReferenceAsync(localWorker, localWorker.AttendanceUserId, validPhotosByAttendanceUserId, now, cancellationToken);
                    photosSynchronizedCount += badgePhotoOutcome.Synchronized;
                    photosCreatedCount += badgePhotoOutcome.Created;
                    photosUpdatedCount += badgePhotoOutcome.Updated;
                    photosUnchangedCount += badgePhotoOutcome.Unchanged;
                    workersWithoutPhotosCount += badgePhotoOutcome.Missing;
                    matchedWorkerIds.Add(localWorker.Id);
                    if (reactivated) reactivatedCount++;

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
                var badgeCreatedPhotoOutcome = await SynchronizePhotoReferenceAsync(newWorker, null, validPhotosByAttendanceUserId, now, cancellationToken);
                photosSynchronizedCount += badgeCreatedPhotoOutcome.Synchronized;
                photosCreatedCount += badgeCreatedPhotoOutcome.Created;
                photosUpdatedCount += badgeCreatedPhotoOutcome.Updated;
                photosUnchangedCount += badgeCreatedPhotoOutcome.Unchanged;
                workersWithoutPhotosCount += badgeCreatedPhotoOutcome.Missing;

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

        var localWorkersOutsideActiveSource = localWorkers
            .Where(worker => !IsRepresentedByActiveSource(worker, sourceRows))
            .ToArray();
        var markedInactiveCount = 0;
        foreach (var worker in localWorkersOutsideActiveSource)
        {
            if (!worker.IsActive && worker.EmploymentStatus == EmploymentStatus.LeftEmployment)
            {
                continue;
            }

            worker.SetEmploymentStatus(EmploymentStatus.LeftEmployment, now, now);
            markedInactiveCount++;
        }

        var missingFromSourceCount = localWorkersOutsideActiveSource.Length;
        completedAtUtc = DateTime.UtcNow;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await RecordAuditAsync(actorUserId, requestMeta, new WorkerInitialSyncResultDto
            {
                SourceCount = sourceCount,
                CreatedCount = createdCount,
                UpdatedCount = updatedCount,
                UnchangedCount = unchangedCount,
                MissingFromSourceCount = missingFromSourceCount,
                MarkedInactiveCount = markedInactiveCount,
                ReactivatedCount = reactivatedCount,
                WarningCount = warningCount,
                PhotosFoundCount = photosFoundCount,
                PhotosSynchronizedCount = photosSynchronizedCount,
                PhotosCreatedCount = photosCreatedCount,
                PhotosUpdatedCount = photosUpdatedCount,
                PhotosUnchangedCount = photosUnchangedCount,
                InvalidOrUnsupportedPhotosCount = invalidOrUnsupportedPhotosCount,
                WorkersWithoutPhotosCount = workersWithoutPhotosCount,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc
            }, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
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
            MarkedInactiveCount = markedInactiveCount,
            ReactivatedCount = reactivatedCount,
            WarningCount = warningCount,
            PhotosFoundCount = photosFoundCount,
            PhotosSynchronizedCount = photosSynchronizedCount,
            PhotosCreatedCount = photosCreatedCount,
            PhotosUpdatedCount = photosUpdatedCount,
            PhotosUnchangedCount = photosUnchangedCount,
            InvalidOrUnsupportedPhotosCount = invalidOrUnsupportedPhotosCount,
            WorkersWithoutPhotosCount = workersWithoutPhotosCount,
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
        out bool changedInData,
        out bool reactivated)
    {
        var normalizedAttendanceUserId = NormalizeString(sourceAttendanceUserId);
        var normalizedName = NormalizeString(sourceName);
        var normalizedBadge = NormalizeString(sourceBadge);
        var validName = !string.IsNullOrWhiteSpace(normalizedName) && !normalizedName.All(char.IsDigit);

        reactivated = !worker.IsActive || worker.EmploymentStatus != EmploymentStatus.Active;
        var hasDataChanges = reactivated ||
            (worker.AttendanceDepartmentId != sourceDepartmentId)
            || (!string.IsNullOrWhiteSpace(normalizedBadge) && !string.Equals(worker.BadgeNumber, normalizedBadge, StringComparison.Ordinal))
            || (validName && !string.Equals(worker.FullName, normalizedName, StringComparison.Ordinal));

        if (reactivated)
        {
            worker.Activate(now);
        }
        worker.ApplyAttendanceSync(now, normalizedAttendanceUserId, validName ? normalizedName : null, normalizedBadge, sourceDepartmentId);
        changedInData = hasDataChanges;
    }

    private async Task<PhotoSyncOutcome> SynchronizePhotoReferenceAsync(
        Worker worker,
        string? attendanceUserId,
        IReadOnlyDictionary<string, AttendanceWorkerPhotoRecord> validPhotosByAttendanceUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var normalizedAttendanceUserId = NormalizeString(attendanceUserId);
        if (normalizedAttendanceUserId is not null &&
            validPhotosByAttendanceUserId.TryGetValue(normalizedAttendanceUserId, out var sourcePhoto))
        {
            var store = await workerPhotoCache.StoreAsync(worker.Id, sourcePhoto.Photo, cancellationToken);
            var managedReference = GetManagedPhotoReference(worker.Id, store.Version);
            if (!string.Equals(worker.PhotoReference, managedReference, StringComparison.Ordinal)) worker.SetPhotoReference(managedReference, now);
            if (store.Unchanged)
            {
                return new PhotoSyncOutcome(Unchanged: 1);
            }
            return new PhotoSyncOutcome(Synchronized: 1, Created: store.Created ? 1 : 0, Updated: store.Updated ? 1 : 0);
        }

        // Only clear planner-managed references. Imported/manual references remain untouched.
        if (IsManagedPhotoReference(worker.PhotoReference, worker.Id))
        {
            worker.SetPhotoReference(null, now);
            await workerPhotoCache.RemoveAsync(worker.Id, cancellationToken);
        }
        return new PhotoSyncOutcome(Missing: 1);
    }

    private static string GetManagedPhotoReference(Guid workerId, string version) => $"/api/workers/{workerId:D}/photo?v={version}";

    private static bool IsManagedPhotoReference(string? photoReference, Guid workerId) =>
        !string.IsNullOrWhiteSpace(photoReference) &&
        photoReference.StartsWith($"/api/workers/{workerId:D}/photo", StringComparison.OrdinalIgnoreCase);

    private readonly record struct PhotoSyncOutcome(int Synchronized = 0, int Created = 0, int Updated = 0, int Unchanged = 0, int Missing = 0);

    private static bool IsRepresentedByActiveSource(Worker worker, IEnumerable<AttendanceEmployeeRecord> sourceRows) =>
        sourceRows.Any(source => IsSourceRowForWorker(source, worker));

    private static bool IsSourceRowForWorker(AttendanceEmployeeRecord source, Worker worker)
    {
        var sourceAttendanceUserId = NormalizeString(source.AttendanceUserId);
        var sourceBadge = NormalizeString(source.BadgeNumber);
        var workerAttendanceUserId = NormalizeString(worker.AttendanceUserId);
        var workerBadge = NormalizeString(worker.BadgeNumber);
        var workerEmployeeCode = NormalizeString(worker.EmployeeCode);

        return (!string.IsNullOrWhiteSpace(sourceAttendanceUserId) &&
                string.Equals(sourceAttendanceUserId, workerAttendanceUserId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(sourceBadge) &&
             (string.Equals(sourceBadge, workerBadge, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(sourceBadge, workerEmployeeCode, StringComparison.OrdinalIgnoreCase)));
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
