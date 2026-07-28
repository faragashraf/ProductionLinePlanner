using System.Data;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.Bootstrap;

/// <summary>
/// Development-only, application-database reset for the first controlled pilot.
/// It has no dependency on, or write path to, the ZKTime attendance source.
/// </summary>
public sealed class PilotMasterDataResetService(
    AppDbContext db,
    IAuditEngine audit) : IPilotMasterDataResetService
{
    private const string ControlledIntakeMigration = "20260715012831_AddControlledRealDataIntake";

    public async Task<PilotMasterDataResetPreviewDto> PreviewAsync(CancellationToken cancellationToken = default)
    {
        var hasControlledIntakeSchema = await HasControlledIntakeSchemaAsync(cancellationToken);
        return new PilotMasterDataResetPreviewDto
        {
            ProductionWorkerAllocations = await db.Set<StageProductionWorkerAllocation>().AsNoTracking().CountAsync(cancellationToken),
            ProductionStageRecords = await db.Set<StageProductionRecord>().AsNoTracking().CountAsync(cancellationToken),
            ProductionOrders = await db.Set<ProductionOrder>().AsNoTracking().CountAsync(cancellationToken),
            ProductionDayStageResolutions = hasControlledIntakeSchema
                ? await db.ProductionDayStageResolutions.AsNoTracking().CountAsync(cancellationToken)
                : 0,
            ImportBatches = hasControlledIntakeSchema
                ? await db.ImportBatches.AsNoTracking().CountAsync(cancellationToken)
                : 0,
            AssignmentTimelineEntries = await db.AssignmentTimelineEntries.AsNoTracking().CountAsync(cancellationToken),
            WorkerTemporaryAssignments = await db.WorkerTemporaryAssignments.AsNoTracking().CountAsync(cancellationToken),
            WorkerDefaultAssignments = await db.WorkerDefaultAssignments.AsNoTracking().CountAsync(cancellationToken),
            StageReadinessSnapshots = await db.StageReadinessSnapshots.AsNoTracking().CountAsync(cancellationToken),
            WorkerSalaryHistories = await db.WorkerSalaryHistories.AsNoTracking().CountAsync(cancellationToken),
            ProductStageMappings = await db.ProductModelStages.AsNoTracking().CountAsync(cancellationToken),
            ProductModels = await db.ProductModels.AsNoTracking().CountAsync(cancellationToken),
            SubStages = await db.SubStages.AsNoTracking().CountAsync(cancellationToken),
            MainStages = await db.MainStages.AsNoTracking().CountAsync(cancellationToken),
            ProductionLines = await db.ProductionLines.AsNoTracking().CountAsync(cancellationToken),
            Departments = await db.Departments.AsNoTracking().CountAsync(cancellationToken),
            Factories = await db.Factories.AsNoTracking().CountAsync(cancellationToken),
            WorkersPreserved = await db.Workers.AsNoTracking().CountAsync(cancellationToken),
            AttendanceRecordsPreserved = await db.AttendanceRecords.AsNoTracking().CountAsync(cancellationToken),
            UsersPreserved = await db.AppUsers.AsNoTracking().CountAsync(cancellationToken),
            RolesPreserved = await db.AppRoles.AsNoTracking().CountAsync(cancellationToken),
            PermissionsPreserved = await db.Permissions.AsNoTracking().CountAsync(cancellationToken),
            ActiveSuperAdminsPreserved = await ActiveSuperAdminCountAsync(cancellationToken)
        };
    }

    public async Task<PilotMasterDataResetApplyResultDto> ApplyAsync(
        Guid actorUserId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException("Pilot reset requires an explicit confirmation flag.");
        }

        await EnsureActiveSuperAdminAsync(actorUserId, cancellationToken);
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        try
        {
            var preview = await PreviewAsync(cancellationToken);
            if (preview.ActiveSuperAdminsPreserved == 0)
            {
                throw new InvalidOperationException("Pilot reset is blocked because no active Super Admin would remain.");
            }

            if (preview.TotalRecordsToDelete == 0)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return new PilotMasterDataResetApplyResultDto(true, preview);
            }

            var hasControlledIntakeSchema = await HasControlledIntakeSchemaAsync(cancellationToken);

            // Delete only application operational/master records, strictly from leaves to roots.
            // Workers, attendance cache, users, roles, permissions, audit history and EF migration
            // history deliberately do not appear in this write set.
            if (hasControlledIntakeSchema)
            {
                db.RemoveRange(await db.ProductionDayStageResolutions.ToListAsync(cancellationToken));
            }
            db.RemoveRange(await db.Set<StageProductionWorkerAllocation>().ToListAsync(cancellationToken));
            db.RemoveRange(await db.Set<StageProductionRecord>().ToListAsync(cancellationToken));
            db.RemoveRange(await db.Set<ProductionOrder>().ToListAsync(cancellationToken));
            if (hasControlledIntakeSchema)
            {
                db.RemoveRange(await db.ImportBatches.ToListAsync(cancellationToken));
            }

            db.RemoveRange(await db.AssignmentTimelineEntries.ToListAsync(cancellationToken));
            db.RemoveRange(await db.WorkerTemporaryAssignments.ToListAsync(cancellationToken));
            db.RemoveRange(await db.WorkerDefaultAssignments.ToListAsync(cancellationToken));
            db.RemoveRange(await db.StageReadinessSnapshots.ToListAsync(cancellationToken));
            db.RemoveRange(await db.WorkerSalaryHistories.ToListAsync(cancellationToken));

            db.RemoveRange(await db.ProductModelStages.ToListAsync(cancellationToken));
            db.RemoveRange(await db.ProductModels.ToListAsync(cancellationToken));
            db.RemoveRange(await db.SubStages.ToListAsync(cancellationToken));
            db.RemoveRange(await db.MainStages.ToListAsync(cancellationToken));
            db.RemoveRange(await db.ProductionLines.ToListAsync(cancellationToken));
            db.RemoveRange(await db.Departments.ToListAsync(cancellationToken));
            db.RemoveRange(await db.Factories.ToListAsync(cancellationToken));

            await audit.RecordAsync(
                actorUserId,
                AuditActionType.Delete,
                "PilotMasterDataReset",
                "application-operational-master-data",
                before: new { preview.TotalRecordsToDelete },
                after: new
                {
                    preview.ProductionWorkerAllocations,
                    preview.ProductionStageRecords,
                    preview.ProductionOrders,
                    preview.AssignmentTimelineEntries,
                    preview.WorkerTemporaryAssignments,
                    preview.WorkerDefaultAssignments,
                    preview.WorkerSalaryHistories,
                    preview.ProductStageMappings,
                    preview.ProductModels,
                    preview.SubStages,
                    preview.MainStages,
                    preview.ProductionLines,
                    preview.Departments,
                    preview.Factories,
                    preview.WorkersPreserved,
                    preview.UsersPreserved,
                    preview.ActiveSuperAdminsPreserved
                },
                requestMeta: "Development-only pilot master-data reset; ZKTime was not accessed.",
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new PilotMasterDataResetApplyResultDto(false, preview);
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<bool> HasControlledIntakeSchemaAsync(CancellationToken cancellationToken)
    {
        try
        {
            var migrations = await db.Database.GetAppliedMigrationsAsync(cancellationToken);
            return migrations.Contains(ControlledIntakeMigration, StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Data.Common.DbException)
        {
            // In-memory test databases created without migrations have no history table. The
            // optional generic-intake tables are not part of the reset unless its migration ran.
            return false;
        }
    }

    private async Task EnsureActiveSuperAdminAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("A Super Admin actor is required for pilot reset.");
        }

        var actor = await db.AppUsers.AsNoTracking().Include(x => x.Roles)
            .SingleOrDefaultAsync(x => x.Id == actorUserId, cancellationToken);
        if (actor is null || !actor.IsActive || !actor.Roles.Any(x => x.IsActive && x.Role == UserRole.SuperAdmin))
        {
            throw new UnauthorizedAccessException("Pilot reset requires an active Super Admin actor.");
        }
    }

    private Task<int> ActiveSuperAdminCountAsync(CancellationToken cancellationToken) =>
        db.AppUsers.AsNoTracking().Include(x => x.Roles)
            .CountAsync(x => x.IsActive && x.Roles.Any(role => role.IsActive && role.Role == UserRole.SuperAdmin), cancellationToken);
}
