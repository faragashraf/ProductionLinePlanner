using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

public sealed class EmployeeMasterDataService(
    AppDbContext dbContext,
    IAuditEngine auditEngine) : IEmployeeMasterDataService
{
    public async Task<PagedResult<WorkerDto>> GetWorkersAsync(
        string? search,
        bool? isActive = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 200)
        {
            return PagedResult<WorkerDto>.Failure(new Error("ValidationError", "page and pageSize must be positive, pageSize max 200."));
        }

        var searchPattern = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        var query = dbContext.Workers.AsNoTracking().AsQueryable();
        if (isActive.HasValue)
        {
            query = query.Where(x => x.IsActive == isActive.Value);
        }

        if (searchPattern is not null)
        {
            query = query.Where(x => EF.Functions.Like(x.EmployeeCode, searchPattern) || EF.Functions.Like(x.FullName, searchPattern));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var entities = await query
            .OrderBy(x => x.FullName)
            .ThenBy(x => x.EmployeeCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        var dtos = await MapWorkersWithAssignmentsAsync(entities, cancellationToken);
            return PagedResult<WorkerDto>.Success(dtos, page, pageSize, totalCount);
    }

    public async Task<Result<WorkerDto>> GetWorkerAsync(Guid workerId, CancellationToken cancellationToken = default)
    {
        if (workerId == Guid.Empty)
        {
            return Result<WorkerDto>.Failure(new Error("ValidationError", "WorkerId is required."));
        }

        var entity = await dbContext.Workers.FirstOrDefaultAsync(x => x.Id == workerId, cancellationToken);
        if (entity is null)
        {
            return Result<WorkerDto>.Failure(new Error("NotFound", "Worker not found."));
        }

        var dtos = await MapWorkersWithAssignmentsAsync([entity], cancellationToken);
        return Result<WorkerDto>.Success(dtos.Single());
    }

    public async Task<Result<WorkerDto>> UpdateMasterIdentityAsync(
        Guid workerId,
        UpdateWorkerRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<WorkerDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (workerId == Guid.Empty)
        {
            return Result<WorkerDto>.Failure(new Error("ValidationError", "WorkerId is required."));
        }

        if (request.FullName is null && request.AttendanceDepartmentId is null && request.Phone is null)
        {
            return Result<WorkerDto>.Failure(new Error("ValidationError", "No updatable fields were provided."));
        }

        var entity = await dbContext.Workers.FirstOrDefaultAsync(x => x.Id == workerId, cancellationToken);
        if (entity is null)
        {
            return Result<WorkerDto>.Failure(new Error("NotFound", "Worker not found."));
        }

        var requestFullName = request.FullName?.Trim();
        if (requestFullName is not null && requestFullName.Length == 0)
        {
            return Result<WorkerDto>.Failure(new Error("ValidationError", "FullName cannot be empty."));
        }

        var requestDepartmentId = request.AttendanceDepartmentId;
        if (requestDepartmentId.HasValue && requestDepartmentId.Value <= 0)
        {
            return Result<WorkerDto>.Failure(new Error("ValidationError", "AttendanceDepartmentId must be greater than zero."));
        }

        if (requestDepartmentId.HasValue)
        {
            return Result<WorkerDto>.Failure(new Error(
                "SourceObservedOnly",
                "Attendance department is source-observed and cannot be changed from Planner."));
        }

        if (request.Phone is not null && string.IsNullOrWhiteSpace(request.Phone))
        {
            return Result<WorkerDto>.Failure(new Error("ValidationError", "Phone cannot be empty."));
        }

        var nameChanged = requestFullName is not null && !string.Equals(entity.FullName, requestFullName, StringComparison.Ordinal);
        var phoneChanged = request.Phone is not null && !string.Equals(entity.Phone, request.Phone.Trim(), StringComparison.Ordinal);

        if (!nameChanged && !phoneChanged)
        {
            return Result<WorkerDto>.Failure(new Error("ValidationError", "No identity changes detected."));
        }

        var before = MapWorker(entity);
        var now = DateTime.UtcNow;

        if (nameChanged)
        {
            entity.UpdateName(requestFullName!, now);
        }

        if (phoneChanged)
        {
            entity.SetPhone(request.Phone!.Trim(), now);
        }

        dbContext.Entry(entity).Property(nameof(Worker.UpdatedAtUtc)).CurrentValue = now;
        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Update,
            nameof(Worker),
            entity.Id.ToString(),
            before: before,
            after: MapWorker(entity),
            requestMeta: requestMeta,
            cancellationToken: cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var result = (await MapWorkersWithAssignmentsAsync([entity], cancellationToken)).Single();
        return Result<WorkerDto>.Success(result);
    }

    public async Task<Result<WorkerDto>> SetEmploymentStatusAsync(
        Guid workerId,
        SetWorkerEmploymentStatusRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<WorkerDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (workerId == Guid.Empty)
        {
            return Result<WorkerDto>.Failure(new Error("ValidationError", "WorkerId is required."));
        }

        if (!Enum.TryParse<EmploymentStatus>(request.EmploymentStatus?.Trim(), true, out var status))
        {
            return Result<WorkerDto>.Failure(new Error("ValidationError", "EmploymentStatus is invalid."));
        }

        var entity = await dbContext.Workers.FirstOrDefaultAsync(x => x.Id == workerId, cancellationToken);
        if (entity is null)
        {
            return Result<WorkerDto>.Failure(new Error("NotFound", "Worker not found."));
        }

        if (entity.EmploymentStatus == status)
        {
            return Result<WorkerDto>.Failure(new Error("ValidationError", "No status changes detected."));
        }

        var now = DateTime.UtcNow;
        var before = MapWorker(entity);

        if (status == EmploymentStatus.LeftEmployment)
        {
            var effectiveEndDate = request.EmploymentEndDate ?? now;
            if (effectiveEndDate < entity.CreatedAtUtc)
            {
                return Result<WorkerDto>.Failure(new Error("ValidationError", "EmploymentEndDate cannot be before Worker creation date."));
            }

            entity.SetEmploymentStatus(status, now, effectiveEndDate);
        }
        else
        {
            entity.SetEmploymentStatus(status, now);
        }

        dbContext.Entry(entity).Property(nameof(Worker.UpdatedAtUtc)).CurrentValue = now;
        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Update,
            nameof(Worker),
            entity.Id.ToString(),
            before: before,
            after: MapWorker(entity),
            requestMeta: requestMeta,
            cancellationToken: cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var result = (await MapWorkersWithAssignmentsAsync([entity], cancellationToken)).Single();
        return Result<WorkerDto>.Success(result);
    }

    private static WorkerDto MapWorker(Worker worker, Guid? defaultSubStageId = null)
    {
        var photoVersion = GetPhotoVersion(worker.PhotoReference);
        var hasManagedPhoto = photoVersion is not null && IsManagedPhotoReference(worker.PhotoReference, worker.Id);
        return new WorkerDto
        {
            Id = worker.Id,
            EmployeeCode = worker.EmployeeCode,
            FullName = worker.FullName,
            AttendanceUserId = worker.AttendanceUserId,
            BadgeNumber = worker.BadgeNumber,
            Phone = worker.Phone,
            AttendanceDepartmentId = worker.AttendanceDepartmentId,
            LocalDepartmentName = worker.LocalDepartmentName,
            EmploymentStatus = worker.EmploymentStatus.ToString(),
            EmploymentEndDate = worker.EmploymentEndDate,
            // Only synchronization-produced cache references are browser-visible.
            // Legacy/manual strings cannot cause a live or arbitrary image request.
            PhotoReference = hasManagedPhoto ? worker.PhotoReference : null,
            HasPhoto = hasManagedPhoto,
            PhotoVersion = hasManagedPhoto ? photoVersion : null,
            IsActive = worker.IsActive,
            DefaultSubStageId = defaultSubStageId
        };
    }

    private static string? GetPhotoVersion(string? photoReference)
    {
        if (string.IsNullOrWhiteSpace(photoReference)) return null;
        var marker = "?v=";
        var index = photoReference.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index >= 0 && index + marker.Length < photoReference.Length
            ? photoReference[(index + marker.Length)..]
            : null;
    }

    private static bool IsManagedPhotoReference(string? photoReference, Guid workerId) =>
        !string.IsNullOrWhiteSpace(photoReference) &&
        photoReference.StartsWith($"/api/workers/{workerId:D}/photo?v=", StringComparison.OrdinalIgnoreCase);

    private async Task<WorkerDto[]> MapWorkersWithAssignmentsAsync(IEnumerable<Worker> workers, CancellationToken cancellationToken)
    {
        var workerIds = workers.Select(x => x.Id).Distinct().ToArray();
        var assignments = workerIds.Length == 0
            ? []
            : await dbContext.WorkerDefaultAssignments
                .AsNoTracking()
                .Where(x => workerIds.Contains(x.WorkerId) && x.IsActive)
                .Select(x => new { x.WorkerId, x.AssignedAt, x.Id, x.SubStageId })
                .ToListAsync(cancellationToken);

        var activeDefaultByWorker = assignments
            .GroupBy(x => x.WorkerId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.AssignedAt)
                    .ThenByDescending(x => x.Id)
                    .First().SubStageId);

        return workers
            .Select(worker =>
            {
                var defaultSubStageId = activeDefaultByWorker.GetValueOrDefault(worker.Id);
                return MapWorker(worker, defaultSubStageId);
            })
            .ToArray();
    }
}
