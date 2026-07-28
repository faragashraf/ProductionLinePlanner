using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

/// <summary>
/// Owns the Planner-only organizational relationship between a worker and a local
/// department. It never touches ZKTime identities or permanent production staffing.
/// </summary>
public sealed class WorkerDepartmentAssignmentEngine(
    AppDbContext dbContext,
    IPermissionService permissionService,
    IAuditEngine auditEngine) : IWorkerDepartmentAssignmentEngine
{
    private static readonly string[] RequiredPermissions = ["workers.manage", "departments.manage"];

    public async Task<Result<WorkerDepartmentAssignmentDto>> AssignAsync(
        Guid workerId,
        Guid departmentId,
        Guid expectedConcurrencyToken,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
            return Failure("Unauthorized", "User context is required.");
        if (workerId == Guid.Empty || departmentId == Guid.Empty || expectedConcurrencyToken == Guid.Empty)
            return Failure("ValidationError", "WorkerId, DepartmentId, and ConcurrencyToken are required.");

        var permissions = await permissionService.GetEffectivePermissionsAsync(actorUserId, cancellationToken);
        if (RequiredPermissions.Any(required => !permissions.Contains(required, StringComparer.OrdinalIgnoreCase)))
            return Failure("Forbidden", "Worker and department management permissions are required.");

        var worker = await dbContext.Workers.FirstOrDefaultAsync(item => item.Id == workerId, cancellationToken);
        if (worker is null)
            return Failure("NotFound", "Worker not found.");

        var department = await dbContext.Departments
            .Include(item => item.Factory)
            .FirstOrDefaultAsync(item => item.Id == departmentId, cancellationToken);
        if (department is null)
            return Failure("NotFound", "Department not found.");
        if (!department.IsActive || department.Factory?.IsActive != true)
            return Failure("ValidationError", "The selected department or its factory is inactive.");

        if (worker.OrganizationalDepartmentConcurrencyToken != expectedConcurrencyToken)
            return Failure("ConcurrencyConflict", "The worker changed while it was being edited. Reload before saving.");
        if (worker.OrganizationalDepartmentId == departmentId)
            return Failure("Conflict", "The worker is already assigned to this department.");

        var before = DepartmentAudit(worker);
        dbContext.Entry(worker)
            .Property(item => item.OrganizationalDepartmentConcurrencyToken)
            .OriginalValue = expectedConcurrencyToken;
        worker.AssignOrganizationalDepartment(departmentId, DateTime.UtcNow);

        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Update,
            nameof(Worker),
            worker.Id.ToString(),
            before,
            DepartmentAudit(worker),
            requestMeta,
            cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure("ConcurrencyConflict", "The worker changed while it was being saved. Reload before retrying.");
        }

        return Result<WorkerDepartmentAssignmentDto>.Success(new(
            worker.Id,
            department.Id,
            department.NameAr,
            department.FactoryId,
            department.Factory!.Name,
            worker.OrganizationalDepartmentConcurrencyToken,
            worker.UpdatedAtUtc));
    }

    private static object DepartmentAudit(Worker worker) => new
    {
        worker.Id,
        worker.OrganizationalDepartmentId,
        worker.OrganizationalDepartmentConcurrencyToken,
        worker.UpdatedAtUtc
    };

    private static Result<WorkerDepartmentAssignmentDto> Failure(string code, string message) =>
        Result<WorkerDepartmentAssignmentDto>.Failure(new Error(code, message));
}
