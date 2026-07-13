using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

public sealed class WorkerCompensationService(
    AppDbContext dbContext,
    IAuditEngine auditEngine) : IWorkerCompensationService
{
    private static readonly DateTime MaxDate = DateTime.MaxValue;

    public async Task<Result<WorkerSalaryHistoryDto>> GetCurrentSalaryAsync(
        Guid workerId,
        CancellationToken cancellationToken = default)
    {
        return await GetCurrentSalaryAsync(workerId, DateTime.UtcNow, cancellationToken);
    }

    public async Task<Result<WorkerSalaryHistoryDto>> GetCurrentSalaryAsync(
        Guid workerId,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default)
    {
        if (asOfUtc == default)
        {
            return Result<WorkerSalaryHistoryDto>.Failure(new Error("ValidationError", "AsOfUtc is required."));
        }

        var current = await GetCurrentSalaryRecordAsync(workerId, asOfUtc, cancellationToken);
        if (current is null)
        {
            return Result<WorkerSalaryHistoryDto>.Failure(new Error("NotFound", "No active salary found for this worker."));
        }

        return Result<WorkerSalaryHistoryDto>.Success(MapSalary(current));
    }

    public async Task<Result<WorkerSalaryHistoryDto[]>> GetSalaryHistoryAsync(
        Guid workerId,
        CancellationToken cancellationToken = default)
    {
        if (workerId == Guid.Empty)
        {
            return Result<WorkerSalaryHistoryDto[]>.Failure(new Error("ValidationError", "WorkerId is required."));
        }

        if (await WorkerExistsAsync(workerId, cancellationToken) is false)
        {
            return Result<WorkerSalaryHistoryDto[]>.Failure(new Error("NotFound", "Worker not found."));
        }

        var records = await dbContext.WorkerSalaryHistories
            .AsNoTracking()
            .Where(x => x.WorkerId == workerId)
            .OrderByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);

        return Result<WorkerSalaryHistoryDto[]>.Success(records.Select(MapSalary).ToArray());
    }

    public async Task<Result<WorkerSalaryHistoryDto>> SetSalaryAsync(
        Guid workerId,
        SetWorkerSalaryRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<WorkerSalaryHistoryDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (workerId == Guid.Empty)
        {
            return Result<WorkerSalaryHistoryDto>.Failure(new Error("ValidationError", "WorkerId is required."));
        }

        if (request.Amount < 0m)
        {
            return Result<WorkerSalaryHistoryDto>.Failure(new Error("ValidationError", "Amount must be greater than or equal to 0."));
        }

        if (request.EffectiveFrom == default)
        {
            return Result<WorkerSalaryHistoryDto>.Failure(new Error("ValidationError", "EffectiveFrom is required."));
        }

        var currencyCode = NormalizeCurrency(request.CurrencyCode);
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            return Result<WorkerSalaryHistoryDto>.Failure(new Error("ValidationError", "CurrencyCode is required."));
        }

        if (!await WorkerExistsAsync(workerId, cancellationToken))
        {
            return Result<WorkerSalaryHistoryDto>.Failure(new Error("NotFound", "Worker not found."));
        }

        // SetSalary is for forward changes to the current salary chain.
        if (request.EffectiveFrom < DateTime.UtcNow.Date)
        {
            return Result<WorkerSalaryHistoryDto>.Failure(new Error(
                "ValidationError",
                "Historical salary changes must use historical correction endpoint."));
        }

        var hasFutureRecords = await dbContext.WorkerSalaryHistories
            .AsNoTracking()
            .AnyAsync(x => x.WorkerId == workerId && x.EffectiveFrom > request.EffectiveFrom, cancellationToken);
        if (hasFutureRecords)
        {
            return Result<WorkerSalaryHistoryDto>.Failure(new Error(
                "Conflict",
                "Cannot set current salary because future salary history exists for this worker."));
        }

        var overlappingCurrent = await dbContext.WorkerSalaryHistories
            .AsNoTracking()
            .AnyAsync(
                x => x.WorkerId == workerId
                    && x.EffectiveTo.HasValue
                    && x.EffectiveFrom < x.EffectiveTo.Value
                    && x.EffectiveFrom < MaxDate
                    && Overlaps(request.EffectiveFrom, null, x.EffectiveFrom, x.EffectiveTo),
                cancellationToken);

        if (overlappingCurrent)
        {
            return Result<WorkerSalaryHistoryDto>.Failure(new Error("Conflict", "Salary effective date overlaps an existing period."));
        }

        var current = await dbContext.WorkerSalaryHistories
            .Where(x => x.WorkerId == workerId && x.EffectiveTo == null)
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

        if (current is not null)
        {
            if (request.EffectiveFrom < current.EffectiveFrom)
            {
                return Result<WorkerSalaryHistoryDto>.Failure(new Error(
                    "ValidationError",
                    "Historical salary changes must use historical correction endpoint."));
            }

            current.Close(request.EffectiveFrom, actorUserId, DateTime.UtcNow);
            dbContext.Entry(current).Property(nameof(WorkerSalaryHistory.UpdatedBy)).CurrentValue = actorUserId;
            dbContext.Entry(current).Property(nameof(WorkerSalaryHistory.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
        }

        var entity = new WorkerSalaryHistory(
            id: Guid.NewGuid(),
            workerId: workerId,
            amount: request.Amount,
            currencyCode: currencyCode,
            effectiveFrom: request.EffectiveFrom,
            effectiveTo: null,
            notes: request.Notes,
            createdBy: actorUserId,
            updatedBy: actorUserId,
            createdAtUtc: DateTime.UtcNow);

        dbContext.WorkerSalaryHistories.Add(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result<WorkerSalaryHistoryDto>.Failure(new Error("Conflict", "A current salary record already exists for this worker."));
        }

        await auditEngine.RecordAsync(
            actorUserId,
            Domain.Enums.AuditActionType.Create,
            nameof(WorkerSalaryHistory),
            entity.Id.ToString(),
            before: current is null ? null : new { current.Id, current.WorkerId, current.EffectiveFrom, current.EffectiveTo, current.Amount },
            after: new { entity.Id, entity.WorkerId, entity.Amount, entity.CurrencyCode, entity.EffectiveFrom, entity.EffectiveTo },
            requestMeta: requestMeta,
            cancellationToken: cancellationToken);

        return Result<WorkerSalaryHistoryDto>.Success(MapSalary(entity));
    }

    public async Task<Result<WorkerSalaryHistoryDto>> AddHistoricalSalaryAsync(
        Guid workerId,
        SetWorkerSalaryHistoryRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<WorkerSalaryHistoryDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (workerId == Guid.Empty)
        {
            return Result<WorkerSalaryHistoryDto>.Failure(new Error("ValidationError", "WorkerId is required."));
        }

        if (request.Amount < 0m)
        {
            return Result<WorkerSalaryHistoryDto>.Failure(new Error("ValidationError", "Amount must be greater than or equal to 0."));
        }

        if (request.EffectiveFrom == default)
        {
            return Result<WorkerSalaryHistoryDto>.Failure(new Error("ValidationError", "EffectiveFrom is required."));
        }

        if (request.EffectiveTo.HasValue && request.EffectiveTo.Value <= request.EffectiveFrom)
        {
            return Result<WorkerSalaryHistoryDto>.Failure(new Error("ValidationError", "EffectiveTo must be after EffectiveFrom."));
        }

        var currencyCode = NormalizeCurrency(request.CurrencyCode);
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            return Result<WorkerSalaryHistoryDto>.Failure(new Error("ValidationError", "CurrencyCode is required."));
        }

        if (!await WorkerExistsAsync(workerId, cancellationToken))
        {
            return Result<WorkerSalaryHistoryDto>.Failure(new Error("NotFound", "Worker not found."));
        }

        if (request.EffectiveTo is null)
        {
            var hasOpen = await dbContext.WorkerSalaryHistories
                .AsNoTracking()
                .AnyAsync(x => x.WorkerId == workerId && x.EffectiveTo == null, cancellationToken);

            if (hasOpen)
            {
                return Result<WorkerSalaryHistoryDto>.Failure(new Error(
                    "Conflict",
                    "Historical correction with open end is not supported while an active salary exists."));
            }
        }

        var requestedFrom = request.EffectiveFrom;
        var requestedTo = request.EffectiveTo;

        var overlaps = await dbContext.WorkerSalaryHistories
            .AsNoTracking()
            .AnyAsync(
                x => x.WorkerId == workerId && Overlaps(requestedFrom, requestedTo, x.EffectiveFrom, x.EffectiveTo),
                cancellationToken);

        if (overlaps)
        {
            return Result<WorkerSalaryHistoryDto>.Failure(new Error("Conflict", "Salary period overlaps an existing history record."));
        }

        var entity = new WorkerSalaryHistory(
            id: Guid.NewGuid(),
            workerId: workerId,
            amount: request.Amount,
            currencyCode: currencyCode,
            effectiveFrom: request.EffectiveFrom,
            effectiveTo: request.EffectiveTo,
            notes: request.Notes,
            createdBy: actorUserId,
            updatedBy: actorUserId,
            createdAtUtc: DateTime.UtcNow);

        dbContext.WorkerSalaryHistories.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEngine.RecordAsync(
            actorUserId,
            Domain.Enums.AuditActionType.Create,
            nameof(WorkerSalaryHistory),
            entity.Id.ToString(),
            before: null,
            after: new { entity.Id, entity.WorkerId, entity.Amount, entity.CurrencyCode, entity.EffectiveFrom, entity.EffectiveTo },
            requestMeta: requestMeta,
            cancellationToken: cancellationToken);

        return Result<WorkerSalaryHistoryDto>.Success(MapSalary(entity));
    }

    private static string? NormalizeCurrency(string? currencyCode) => string.IsNullOrWhiteSpace(currencyCode)
        ? "EGP"
        : currencyCode.Trim().ToUpperInvariant();

    private async Task<WorkerSalaryHistory?> GetCurrentSalaryRecordAsync(Guid workerId, DateTime asOfUtc, CancellationToken cancellationToken)
    {
        if (await WorkerExistsAsync(workerId, cancellationToken) is false)
        {
            return null;
        }

        return await dbContext.WorkerSalaryHistories
            .AsNoTracking()
            .Where(x => x.WorkerId == workerId && x.EffectiveFrom <= asOfUtc &&
                (x.EffectiveTo == null || x.EffectiveTo > asOfUtc))
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<bool> WorkerExistsAsync(Guid workerId, CancellationToken cancellationToken)
    {
        return await dbContext.Workers.AnyAsync(x => x.Id == workerId, cancellationToken);
    }

    private static bool Overlaps(DateTime fromA, DateTime? toA, DateTime fromB, DateTime? toB)
    {
        var startA = fromA;
        var endA = toA ?? MaxDate;
        var startB = fromB;
        var endB = toB ?? MaxDate;

        return startA < endB && startB < endA;
    }

    private static WorkerSalaryHistoryDto MapSalary(WorkerSalaryHistory source) => new()
    {
        Id = source.Id,
        WorkerId = source.WorkerId,
        Amount = source.Amount,
        CurrencyCode = source.CurrencyCode,
        EffectiveFrom = source.EffectiveFrom,
        EffectiveTo = source.EffectiveTo,
        Notes = source.Notes,
        CreatedBy = source.CreatedBy,
        UpdatedBy = source.UpdatedBy,
        CreatedAtUtc = source.CreatedAtUtc,
        UpdatedAtUtc = source.UpdatedAtUtc
    };
}
