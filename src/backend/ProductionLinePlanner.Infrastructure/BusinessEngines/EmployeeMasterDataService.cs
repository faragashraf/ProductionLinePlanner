using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
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
    IAttendanceEmployeeWriter attendanceEmployeeWriter,
    IAttendanceEmployeeReader attendanceEmployeeReader,
    IAttendanceDepartmentReader attendanceDepartmentReader,
    IAuditEngine auditEngine) : IEmployeeMasterDataService
{
    public async Task<PagedResult<WorkerDto>> GetWorkersAsync(
        string? search,
        bool? isActive = true,
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

        if (request.Phone is not null && string.IsNullOrWhiteSpace(request.Phone))
        {
            return Result<WorkerDto>.Failure(new Error("ValidationError", "Phone cannot be empty."));
        }

        var nameChanged = requestFullName is not null && !string.Equals(entity.FullName, requestFullName, StringComparison.Ordinal);
        var departmentChanged = requestDepartmentId is not null && entity.AttendanceDepartmentId != requestDepartmentId.Value;
        var phoneChanged = request.Phone is not null && !string.Equals(entity.Phone, request.Phone.Trim(), StringComparison.Ordinal);

        if (!nameChanged && !departmentChanged && !phoneChanged)
        {
            return Result<WorkerDto>.Failure(new Error("ValidationError", "No identity changes detected."));
        }

        var updatedFromAttendance = false;
        var before = MapWorker(entity);
        var now = DateTime.UtcNow;
        var previousDepartmentId = entity.AttendanceDepartmentId;

        if (nameChanged || departmentChanged)
        {
            if (string.IsNullOrWhiteSpace(entity.AttendanceUserId))
            {
                return Result<WorkerDto>.Failure(new Error("ValidationError", "Worker is not linked to attendance source."));
            }

            var syncResult = await UpdateAttendanceIdentityAsync(entity.AttendanceUserId, requestFullName, requestDepartmentId, cancellationToken);
            if (syncResult.IsFailure)
            {
                return Result<WorkerDto>.Failure(syncResult.Error!);
            }

            updatedFromAttendance = true;
        }

        try
        {
            if (nameChanged)
            {
                entity.UpdateName(requestFullName!, now);
            }

            if (departmentChanged)
            {
                var department = await attendanceDepartmentReader.GetByIdAsync(requestDepartmentId!.Value, cancellationToken);
                if (department.IsFailure || department.Value is null)
                {
                    return Result<WorkerDto>.Failure(new Error("NotFound", "Department was not found in attendance source."));
                }

                entity.SetAttendanceDepartmentId(requestDepartmentId, now);
            }

            if (phoneChanged)
            {
                entity.SetPhone(request.Phone!.Trim(), now);
            }

            dbContext.Entry(entity).Property(nameof(Worker.LastExternalSyncAt)).CurrentValue = now;
            dbContext.Entry(entity).Property(nameof(Worker.UpdatedAtUtc)).CurrentValue = now;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            if (updatedFromAttendance && string.IsNullOrWhiteSpace(entity.AttendanceUserId) is false)
            {
                if (nameChanged)
                {
                    _ = await attendanceEmployeeWriter.UpdateWorkerFullNameAsync(entity.AttendanceUserId!, before.FullName, cancellationToken);
                }

                if (departmentChanged)
                {
                    if (previousDepartmentId.HasValue)
                    {
                        _ = await attendanceEmployeeWriter.UpdateWorkerDepartmentAsync(entity.AttendanceUserId!, previousDepartmentId.Value, cancellationToken);
                    }
                }
            }

            throw;
        }

        var after = MapWorker(entity);
        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Update,
            nameof(Worker),
            entity.Id.ToString(),
            before: before,
            after: after,
            requestMeta: requestMeta);

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
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Update,
            nameof(Worker),
            entity.Id.ToString(),
            before: before,
            after: MapWorker(entity),
            requestMeta: requestMeta);

        var result = (await MapWorkersWithAssignmentsAsync([entity], cancellationToken)).Single();
        return Result<WorkerDto>.Success(result);
    }

    private async Task<Result> UpdateAttendanceIdentityAsync(
        string attendanceUserId,
        string? fullName,
        int? attendanceDepartmentId,
        CancellationToken cancellationToken)
    {
        var normalizedAttendanceUserId = attendanceUserId.Trim();
        var updateNameRequired = fullName is not null;
        var updateDepartmentRequired = attendanceDepartmentId.HasValue;

        if (updateNameRequired)
        {
            var name = fullName!.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return Result.Failure(new Error("ValidationError", "FullName cannot be empty."));
            }

            var updateResult = await attendanceEmployeeWriter.UpdateWorkerFullNameAsync(normalizedAttendanceUserId, name, cancellationToken);
            if (updateResult.IsFailure)
            {
                return Result.Failure(updateResult.Error!);
            }
        }

        if (updateDepartmentRequired)
        {
            var departmentResult = await attendanceEmployeeWriter.UpdateWorkerDepartmentAsync(
                normalizedAttendanceUserId,
                attendanceDepartmentId!.Value,
                cancellationToken);

            if (departmentResult.IsFailure)
            {
                if (updateNameRequired)
                {
                    var current = await attendanceEmployeeReader.GetByAttendanceUserIdAsync(normalizedAttendanceUserId, cancellationToken);
                    if (current.IsSuccess && current.Value is not null && !string.IsNullOrWhiteSpace(current.Value.Name))
                    {
                        _ = await attendanceEmployeeWriter.UpdateWorkerFullNameAsync(
                            normalizedAttendanceUserId,
                            current.Value.Name,
                            cancellationToken);
                    }
                }

                return Result.Failure(departmentResult.Error!);
            }
        }

        return Result.Success();
    }

    private static WorkerDto MapWorker(Worker worker, Guid? defaultSubStageId = null) => new()
    {
        Id = worker.Id,
        EmployeeCode = worker.EmployeeCode,
        FullName = worker.FullName,
        AttendanceUserId = worker.AttendanceUserId,
        BadgeNumber = worker.BadgeNumber,
        Phone = worker.Phone,
        AttendanceDepartmentId = worker.AttendanceDepartmentId,
        EmploymentStatus = worker.EmploymentStatus.ToString(),
        EmploymentEndDate = worker.EmploymentEndDate,
        PhotoReference = worker.PhotoReference,
        IsActive = worker.IsActive,
        DefaultSubStageId = defaultSubStageId
    };

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
