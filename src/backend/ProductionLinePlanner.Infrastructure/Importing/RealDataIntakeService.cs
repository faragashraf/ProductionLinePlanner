using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.Importing;

/// <summary>
/// Controlled, read-then-apply intake for the pilot workbooks. Preview never
/// writes to AppDbContext; Apply reparses the same bytes and uses a source hash
/// as its idempotency key.
/// </summary>
public sealed class RealDataIntakeService(
    AppDbContext db,
    IImportNormalizationService normalizer,
    IAssignmentEngine assignmentEngine,
    IAuditEngine audit,
    ICairoTimeZoneProvider cairoTimeZoneProvider) : IRealDataIntakeService
{
    private const string Blocking = "blocking";
    private const string Warning = "warning";
    public async Task<RealDataIntakePreviewDto> PreviewAsync(RealDataIntakeUpload upload, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(upload, cancellationToken);
        return ToPreview(prepared);
    }

    public async Task<RealDataIntakeApplyResultDto> ApplyAsync(RealDataIntakeUpload upload, Guid actorId, CancellationToken cancellationToken = default)
    {
        if (actorId == Guid.Empty) throw new UnauthorizedAccessException("User context is required.");
        var prepared = await PrepareAsync(upload, cancellationToken);
        if (prepared.HasBlockingIssues)
            throw new ProductionConflictException("لا يمكن تطبيق الاستيراد قبل حل مشكلات المعاينة المانعة.");

        var existingBatch = await db.ImportBatches.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == prepared.IdempotencyKey, cancellationToken);
        if (existingBatch is not null)
        {
            var existingOrders = await db.Set<ProductionOrder>().AsNoTracking()
                .Where(x => x.SourceImportBatchId == existingBatch.Id)
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken);
            var existingRecords = await db.Set<StageProductionRecord>().AsNoTracking()
                .Where(x => existingOrders.Contains(x.ProductionOrderId))
                .Select(x => new { x.Id, x.ProductionOrderId })
                .ToArrayAsync(cancellationToken);
            var allocationCount = await db.Set<StageProductionWorkerAllocation>().AsNoTracking()
                .CountAsync(x => existingRecords.Select(record => record.Id).Contains(x.StageProductionRecordId), cancellationToken);
            return new RealDataIntakeApplyResultDto(existingBatch.Id.ToString(), prepared.IdempotencyKey, true, 0, 0, 0, 0, 0, allocationCount, await CountOpenReviewIssuesAsync(existingOrders, cancellationToken));
        }

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        try
        {
            var now = DateTime.UtcNow;
            var batch = new ImportBatch(Guid.NewGuid(), prepared.IdempotencyKey, prepared.SourceReference, actorId, now);
            db.ImportBatches.Add(batch);

            var stageApply = await ApplyStagesAndMappingsAsync(prepared, now, cancellationToken);
            var workerUpdateCount = await ApplyWorkerProjectionAsync(prepared, actorId, now, cancellationToken);
            var productionApply = await ApplyProductionDaysAsync(prepared, batch, actorId, now, cancellationToken);

            await audit.RecordAsync(actorId, AuditActionType.Create, nameof(ImportBatch), batch.Id.ToString(), null,
                new { batch.Id, batch.Status, StageRows = prepared.Stages.Count, WorkerRows = prepared.Workers.Count, ProductionRows = prepared.ProductionRows.Count, ProductionDays = productionApply.DaysCreated },
                "RealDataIntake", cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);

            var orderIds = productionApply.OrderIds.ToArray();
            return new RealDataIntakeApplyResultDto(batch.Id.ToString(), prepared.IdempotencyKey, false, stageApply.Created, stageApply.Updated, workerUpdateCount, productionApply.DaysCreated, productionApply.RecordsCreated, productionApply.AllocationsCreated, await CountOpenReviewIssuesAsync(orderIds, cancellationToken));
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ProductionDayReviewDto> GetProductionDayReviewAsync(Guid productionOrderId, CancellationToken cancellationToken = default)
    {
        var order = await db.Set<ProductionOrder>()
            .AsNoTracking()
            .Include(x => x.ProductModel)
            .Include(x => x.ProductionLine).ThenInclude(x => x!.Factory)
            .Include(x => x.StageProductionRecords).ThenInclude(x => x.WorkerAllocations)
            .Include(x => x.StageResolutions)
            .SingleOrDefaultAsync(x => x.Id == productionOrderId, cancellationToken)
            ?? throw new KeyNotFoundException("Production day was not found.");
        if (!order.ProductionLineId.HasValue || order.ProductionLine?.Factory is null || order.ProductModel is null)
            throw new ProductionConflictException("Production day has an incomplete factory, line, or product mapping.");

        var requiredStages = await db.Set<ProductModelStage>().AsNoTracking()
            .Where(x => x.ProductModelId == order.ProductModelId && x.IsActive && x.IsRequired)
            .Include(x => x.SubStage)
            .OrderBy(x => x.StageOrder)
            .ToArrayAsync(cancellationToken);

        var recordsByStage = order.StageProductionRecords.ToDictionary(x => x.ProductModelStageId);
        var resolutionsByStage = order.StageResolutions.ToDictionary(x => x.ProductModelStageId);
        var issues = new List<ProductionDayReviewIssueDto>();

        foreach (var requiredStage in requiredStages)
        {
            if (recordsByStage.TryGetValue(requiredStage.Id, out var record))
            {
                if (record.ProducedQuantity != order.PlannedQuantity || record.AcceptedQuantity != order.PlannedQuantity || record.RejectedQuantity != 0m)
                {
                    issues.Add(new ProductionDayReviewIssueDto(requiredStage.Id, record.SnapshotStageCode, record.SnapshotStageName, "Open", "Stage quantity must equal the final line quantity exactly once.", null));
                }
                continue;
            }

            if (resolutionsByStage.TryGetValue(requiredStage.Id, out var resolution))
            {
                issues.Add(new ProductionDayReviewIssueDto(requiredStage.Id, requiredStage.SubStage!.Code, requiredStage.SubStage.Name, "Resolved", "Manager marked this required stage as intentionally not operated for the day.", resolution.Reason));
                continue;
            }

            issues.Add(new ProductionDayReviewIssueDto(requiredStage.Id, requiredStage.SubStage!.Code, requiredStage.SubStage.Name, "Open", "Required product stage is missing. Add allocations or mark it intentionally not operated with a reason.", null));
        }

        foreach (var record in order.StageProductionRecords)
        {
            if (record.WorkerAllocations.Select(x => x.WorkerId).Distinct().Count() != record.WorkerAllocations.Count)
                issues.Add(new ProductionDayReviewIssueDto(record.ProductModelStageId, record.SnapshotStageCode, record.SnapshotStageName, "Open", "A worker appears more than once in one stage allocation.", null));
            if (record.TotalWorkerEarnings != 0m && record.TotalWorkerEarnings != decimal.Round(record.WorkerAllocations.Sum(x => x.CalculatedEarning), 4, MidpointRounding.AwayFromZero))
                issues.Add(new ProductionDayReviewIssueDto(record.ProductModelStageId, record.SnapshotStageCode, record.SnapshotStageName, "Open", "Persisted stage total does not equal the allocation sum.", null));
        }

        var allocations = order.StageProductionRecords
            .OrderBy(record => record.SnapshotStageCode)
            .SelectMany(record => record.WorkerAllocations.OrderBy(allocation => allocation.SnapshotWorkerCode)
                .Select(allocation => new ProductionDayReviewAllocationDto(record.Id, record.ProductModelStageId, record.SnapshotStageCode, record.SnapshotStageName, allocation.WorkerId, allocation.SnapshotWorkerCode, allocation.SnapshotWorkerName, allocation.InputQuantity, allocation.CalculatedEarning, allocation.ManualOverrideReason)))
            .ToArray();
        return new ProductionDayReviewDto(order.Id, order.ProductionDate, order.RecordedAtUtc, order.Status == ProductionOrderStatus.Completed ? "Approved" : order.Status.ToString(), order.PlannedQuantity, order.ProductionLine.Factory.Name, order.ProductionLine.Name, order.ProductModel.Name, order.StageProductionRecords.Count, order.StageProductionRecords.Sum(x => x.WorkerAllocations.Count), issues, allocations);
    }

    public async Task<ProductionDayReviewDto> MarkStageNotOperatedAsync(Guid productionOrderId, Guid productModelStageId, string reason, Guid actorId, CancellationToken cancellationToken = default)
    {
        if (actorId == Guid.Empty) throw new UnauthorizedAccessException("User context is required.");
        var order = await db.Set<ProductionOrder>().Include(x => x.StageProductionRecords).Include(x => x.StageResolutions)
            .SingleOrDefaultAsync(x => x.Id == productionOrderId, cancellationToken)
            ?? throw new KeyNotFoundException("Production day was not found.");
        if (order.Status != ProductionOrderStatus.Draft) throw new ProductionConflictException("Only a draft production day can be resolved.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ProductionConflictException("A reason is required when a stage did not operate.");
        if (order.StageProductionRecords.Any(x => x.ProductModelStageId == productModelStageId)) throw new ProductionConflictException("A stage with production cannot be marked as not operated.");
        var required = await db.Set<ProductModelStage>().AnyAsync(x => x.Id == productModelStageId && x.ProductModelId == order.ProductModelId && x.IsActive && x.IsRequired, cancellationToken);
        if (!required) throw new ProductionConflictException("Only a required product stage may be resolved this way.");
        if (!order.StageResolutions.Any(x => x.ProductModelStageId == productModelStageId))
            db.Add(new ProductionDayStageResolution(Guid.NewGuid(), order.Id, productModelStageId, reason, actorId, DateTime.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
        return await GetProductionDayReviewAsync(order.Id, cancellationToken);
    }

    public async Task<ProductionDayReviewDto> SetParticipantOverrideAsync(Guid productionOrderId, Guid stageProductionRecordId, Guid workerId, string reason, Guid actorId, CancellationToken cancellationToken = default)
    {
        if (actorId == Guid.Empty) throw new UnauthorizedAccessException("User context is required.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ProductionConflictException("A reason is required for an attendance or assignment override.");
        var order = await db.Set<ProductionOrder>()
            .Include(x => x.StageProductionRecords).ThenInclude(x => x.WorkerAllocations)
            .SingleOrDefaultAsync(x => x.Id == productionOrderId, cancellationToken)
            ?? throw new KeyNotFoundException("Production day was not found.");
        if (order.Status != ProductionOrderStatus.Draft) throw new ProductionConflictException("Only a draft production day can receive a participant override.");
        var record = order.StageProductionRecords.SingleOrDefault(x => x.Id == stageProductionRecordId)
            ?? throw new ProductionConflictException("The stage record does not belong to this production day.");
        var allocation = record.WorkerAllocations.SingleOrDefault(x => x.WorkerId == workerId)
            ?? throw new ProductionConflictException("The worker allocation does not belong to this stage record.");
        allocation.SetManualOverrideReason(reason);
        await audit.RecordAsync(actorId, AuditActionType.Update, nameof(StageProductionWorkerAllocation), allocation.Id.ToString(), null,
            new { productionOrderId, stageProductionRecordId, workerId, OverrideApplied = true }, "RealDataIntakeParticipantOverride", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetProductionDayReviewAsync(order.Id, cancellationToken);
    }

    public async Task<ProductionDayReviewDto> ApproveProductionDayAsync(Guid productionOrderId, Guid actorId, CancellationToken cancellationToken = default)
    {
        if (actorId == Guid.Empty) throw new UnauthorizedAccessException("User context is required.");
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var review = await GetProductionDayReviewAsync(productionOrderId, cancellationToken);
        if (review.Issues.Any(x => x.Status == "Open")) throw new ProductionConflictException("The daily production record still has unresolved review issues.");

        var order = await db.Set<ProductionOrder>().Include(x => x.StageProductionRecords).ThenInclude(x => x.WorkerAllocations)
            .SingleAsync(x => x.Id == productionOrderId, cancellationToken);
        if (order.Status == ProductionOrderStatus.Completed) return review;
        if (order.Status != ProductionOrderStatus.Draft) throw new ProductionConflictException("Only an imported draft day can be approved.");

        await EnsureAttendanceAndAssignmentEvidenceAsync(order, cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var record in order.StageProductionRecords)
        {
            var total = decimal.Round(record.WorkerAllocations.Sum(x => x.CalculatedEarning), 4, MidpointRounding.AwayFromZero);
            record.Approve(total, actorId, now);
        }
        order.ApproveDay(actorId, now);
        await audit.RecordAsync(actorId, AuditActionType.Update, nameof(ProductionOrder), order.Id.ToString(), null,
            new { order.Id, order.ProductionDate, order.PlannedQuantity, Status = "Approved", StageCount = order.StageProductionRecords.Count }, "RealDataIntakeApproval", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return await GetProductionDayReviewAsync(order.Id, cancellationToken);
    }

    private async Task<PreparedIntake> PrepareAsync(RealDataIntakeUpload upload, CancellationToken ct)
    {
        ValidateFile(upload.StagesWorkbook, "stages");
        ValidateFile(upload.SalaryWorkbook, "salary");
        ValidateFile(upload.ProductionWorkbook, "production");
        var issues = new List<IntakeIssueDto>();
        var stages = ParseStages(upload.StagesWorkbook, issues);
        var workers = ParseSalaries(upload.SalaryWorkbook, issues);
        var productionRows = ParseProduction(upload.ProductionWorkbook, issues);
        var idempotencyKey = ComputeIdempotencyKey(upload);
        var sourceReference = $"RealDataIntake/{idempotencyKey[..16]}";

        var factories = await db.Factories.AsNoTracking().Where(x => x.IsActive).ToArrayAsync(ct);
        var factory = MatchExactly(factories, upload.FactoryName, x => x.Name, "Factory", issues);
        var lines = factory is null ? [] : await db.ProductionLines.AsNoTracking().Where(x => x.FactoryId == factory.Id && x.IsActive).ToArrayAsync(ct);
        var line = MatchExactly(lines, upload.ProductionLineName, x => x.Name, "ProductionLine", issues);
        var products = await db.Set<ProductModel>().AsNoTracking().Where(x => x.IsActive).ToArrayAsync(ct);
        var product = MatchExactly(products, upload.ProductName, x => x.Name, "Product", issues);

        var existingSubStages = line is null ? [] : await db.SubStages.AsNoTracking().Include(x => x.MainStage)
            .Where(x => x.IsActive && x.MainStage!.IsActive && x.MainStage.ProductionLineId == line.Id).ToArrayAsync(ct);
        var mappings = product is null ? [] : await db.Set<ProductModelStage>().AsNoTracking().Include(x => x.SubStage)
            .Where(x => x.ProductModelId == product.Id && x.IsActive).ToArrayAsync(ct);
        var mappingBySubStage = mappings.GroupBy(x => x.SubStageId).ToDictionary(x => x.Key, x => x.ToArray());
        if (line is not null && product is not null && upload.ProductionDayQuantities.Count > 0)
        {
            var importedDates = upload.ProductionDayQuantities.Select(x => x.ProductionDate).Distinct().ToArray();
            var existingDays = await db.Set<ProductionOrder>().AsNoTracking()
                .Where(x => x.ProductionLineId == line.Id && x.ProductModelId == product.Id && importedDates.Contains(x.ProductionDate))
                .Select(x => new { x.ProductionDate, x.SourceImportBatchId })
                .ToArrayAsync(ct);
            var alreadyApplied = await db.ImportBatches.AsNoTracking().AnyAsync(x => x.IdempotencyKey == idempotencyKey, ct);
            if (!alreadyApplied)
            {
                foreach (var existingDay in existingDays)
                    issues.Add(Block("ExistingProductionDayConflict", $"A production-day aggregate already exists for {existingDay.ProductionDate:yyyy-MM-dd}."));
            }
        }

        ValidateSourceStageIdentities(stages, issues);
        var usedCodes = existingSubStages.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var generatedCode = 4;
        var stagePlans = new List<StagePlan>();
        foreach (var source in stages.OrderBy(x => x.SourceRow))
        {
            var matches = existingSubStages.Where(x => StageIdentity(x.MainStage!.Name, x.Name) == StageIdentity(source.MainStageName, source.SubStageName)).ToArray();
            var planIssues = new List<IntakeIssueDto>();
            if (matches.Length > 1) planIssues.Add(Block("AmbiguousStageIdentity", "More than one existing stage matches this normalized identity.", source.SourceRow));
            var existing = matches.Length == 1 ? matches[0] : null;
            var code = string.IsNullOrWhiteSpace(source.SourceCode) ? existing?.Code : source.SourceCode!.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                do { code = $"STG{generatedCode++:000}"; } while (usedCodes.Contains(code));
            }
            var codeOwner = existingSubStages.Where(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (codeOwner.Length > 0 && (existing is null || codeOwner.Any(x => x.Id != existing.Id))) planIssues.Add(Block("DuplicateStageCode", "Stage code is already assigned to a different stage.", source.SourceRow));
            if (!usedCodes.Add(code) && existing is null) planIssues.Add(Block("DuplicateStageCodeInWorkbook", "Stage code appears more than once in the stages workbook.", source.SourceRow));
            var mapping = existing is not null && mappingBySubStage.TryGetValue(existing.Id, out var candidates) && candidates.Length == 1 ? candidates[0] : null;
            if (existing is not null && mappingBySubStage.TryGetValue(existing.Id, out candidates) && candidates.Length > 1) planIssues.Add(Block("AmbiguousProductStageMapping", "More than one product-stage mapping exists for the stage.", source.SourceRow));
            if (existing is null) planIssues.Add(Block("StageRequiresMapping", "A new stage cannot receive a product mapping until its authoritative compensation configuration exists.", source.SourceRow));
            if (existing is not null && mapping is null) planIssues.Add(Block("MissingCompensationConfiguration", "No existing compensation configuration exists for this product stage.", source.SourceRow));
            stagePlans.Add(new StagePlan(source, existing, mapping, code, planIssues));
        }

        var appWorkers = await db.Workers.AsNoTracking().ToArrayAsync(ct);
        var workersByCode = appWorkers.GroupBy(x => normalizer.NormalizeEmployeeCode(x.EmployeeCode)).ToDictionary(x => x.Key, x => x.ToArray());
        ValidateDuplicateEmployeeCodes(workers, issues);
        var salaryByWorker = await db.Set<WorkerSalaryHistory>().AsNoTracking().Where(x => x.EffectiveTo == null).ToArrayAsync(ct);
        var workerPlans = workers.Select(source =>
        {
            var code = normalizer.NormalizeEmployeeCode(source.EmployeeCode);
            var matches = workersByCode.GetValueOrDefault(code, []);
            var rowIssues = new List<IntakeIssueDto>();
            if (matches.Length == 0) rowIssues.Add(Block("UnmatchedEmployeeCode", "No application worker matched this employee code.", source.SourceRow));
            if (matches.Length > 1) rowIssues.Add(Block("DuplicateApplicationEmployeeCode", "More than one application worker matched this employee code.", source.SourceRow));
            var worker = matches.Length == 1 ? matches[0] : null;
            var current = worker is null ? null : salaryByWorker.Where(x => x.WorkerId == worker.Id).OrderByDescending(x => x.EffectiveFrom).FirstOrDefault()?.Amount;
            return new WorkerPlan(source, worker, current, rowIssues);
        }).ToList();

        var stageLookup = stagePlans.ToArray();
        var productionPlans = new List<ProductionPlan>();
        foreach (var source in productionRows)
        {
            var rowIssues = new List<IntakeIssueDto>();
            var stageMatches = stageLookup.Where(x => MatchesProductionStage(x, source.StageName)).ToArray();
            if (stageMatches.Length == 0) rowIssues.Add(Block("UnmatchedProductionStage", "No normalized stage match was found for the production row.", source.SourceRow));
            if (stageMatches.Length > 1) rowIssues.Add(Block("AmbiguousProductionStage", "More than one stage matched this production row.", source.SourceRow));
            var stage = stageMatches.Length == 1 ? stageMatches[0] : null;
            var workerMatches = workersByCode.GetValueOrDefault(normalizer.NormalizeEmployeeCode(source.EmployeeCode), []);
            if (workerMatches.Length == 0) rowIssues.Add(Block("UnmappedProductionWorker", "Production worker code is not mapped to an application worker.", source.SourceRow));
            if (workerMatches.Length > 1) rowIssues.Add(Block("AmbiguousProductionWorker", "Production worker code maps to more than one application worker.", source.SourceRow));
            var worker = workerMatches.Length == 1 ? workerMatches[0] : null;
            if (!source.InputQuantity.HasValue) rowIssues.Add(Block("MissingAllocationQuantity", "Worker allocation quantity is required and cannot be inferred.", source.SourceRow));
            if (stage?.Mapping is not null)
            {
                if (stage.Mapping.CompensationMode == CompensationMode.SharedPercentage && !source.Percentage.HasValue)
                    rowIssues.Add(Block("MissingAllocationPercentage", "The configured shared-percentage mode requires an explicit percentage from the workbook.", source.SourceRow));
                if (stage.Mapping.CompensationMode == CompensationMode.FixedAmount && !source.FixedAmount.HasValue)
                    rowIssues.Add(Block("MissingFixedAllocationAmount", "The configured fixed-amount mode requires an explicit amount from the workbook.", source.SourceRow));
            }
            productionPlans.Add(new ProductionPlan(source, stage, worker, rowIssues));
        }

        ValidateProductionGroups(productionPlans, upload.ProductionDayQuantities, issues);
        var missingStages = BuildMissingStages(product, mappings, productionPlans, upload.ProductionDayQuantities);
        foreach (var missing in missingStages) issues.Add(new IntakeIssueDto(Warning, "MissingProductStage", missing.Message, null));
        await AddAttendanceReadinessIssuesAsync(productionPlans, ct);

        return new PreparedIntake(upload, idempotencyKey, sourceReference, factory, line, product, stagePlans, workerPlans, productionPlans, missingStages, issues);
    }

    private RealDataIntakePreviewDto ToPreview(PreparedIntake prepared) => new()
    {
        IdempotencyKey = prepared.IdempotencyKey,
        CanApply = !prepared.HasBlockingIssues,
        ParsedStageRows = prepared.Stages.Count,
        ParsedWorkerRows = prepared.Workers.Count,
        ParsedProductionWorkerRows = prepared.ProductionRows.Count,
        Stages = prepared.Stages.Select(x => new StageIntakePreviewRowDto(x.Source.SourceRow, x.Code, x.Source.MainStageName, x.Source.SubStageName, x.Source.PiecePrice, x.Source.StandardSeconds, x.Issues.Count > 0 ? "blocked" : x.Existing is null ? "create" : x.Mapping is not null && (x.Mapping.PiecePrice != x.Source.PiecePrice || x.Mapping.StandardSeconds != x.Source.StandardSeconds) ? "update" : "unchanged", x.Issues)).ToArray(),
        ProductStageMappings = prepared.Stages.Select(x => new ProductStageMappingPreviewRowDto(x.Code, x.Source.MainStageName, x.Source.SubStageName, x.Issues.Any(issue => issue.Code.Contains("Compensation", StringComparison.Ordinal)) ? "blocked" : x.Mapping is null ? "blocked" : x.Mapping.PiecePrice != x.Source.PiecePrice || x.Mapping.StandardSeconds != x.Source.StandardSeconds ? "update" : "unchanged", x.Issues.Where(issue => issue.Code.Contains("Mapping", StringComparison.Ordinal) || issue.Code.Contains("Compensation", StringComparison.Ordinal)).ToArray())).ToArray(),
        Workers = prepared.Workers.Select(x => new WorkerIntakePreviewRowDto(x.Source.SourceRow, x.Source.EmployeeCode, x.Source.SourceName, x.Worker?.EmployeeCode, x.Worker?.LocalDepartmentName, x.Source.DepartmentName, x.CurrentSalary, x.Source.Salary, x.Issues.Count > 0 ? "blocked" : x.Worker!.LocalDepartmentName != x.Source.DepartmentName || x.CurrentSalary != x.Source.Salary ? "update" : "unchanged", x.Issues)).ToArray(),
        ProductionDays = prepared.Upload.ProductionDayQuantities.OrderBy(x => x.ProductionDate).Select(x => new ProductionDayHeaderPreviewDto(x.ProductionDate, x.LineQuantity, prepared.ProductionPlans.Any(row => row.Source.ProductionDate == x.ProductionDate && row.Issues.Count > 0) ? "blocked" : "create", prepared.Issues.Where(issue => issue.SourceRow is null && issue.Message.Contains(x.ProductionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)).ToArray())).ToArray(),
        ProductionStages = prepared.ProductionPlans.GroupBy(x => new { x.Source.ProductionDate, Stage = x.Stage?.Code ?? x.Source.StageName }).Select(group => new ProductionStagePreviewRowDto(group.Key.ProductionDate, group.Min(x => x.Source.SourceRow), group.First().Source.StageName, group.First().Stage?.Code, group.Count(), group.Any(x => x.Issues.Count > 0) ? "blocked" : "create", group.SelectMany(x => x.Issues).ToArray())).OrderBy(x => x.ProductionDate).ThenBy(x => x.SourceRow).ToArray(),
        MissingProductStages = prepared.MissingStages,
        Issues = prepared.AllIssues
    };

    private async Task<(int Created, int Updated)> ApplyStagesAndMappingsAsync(PreparedIntake prepared, DateTime now, CancellationToken ct)
    {
        var preparedLine = prepared.Line ?? throw new ProductionConflictException("Production line mapping is required before apply.");
        var preparedProduct = prepared.Product ?? throw new ProductionConflictException("Product mapping is required before apply.");
        var mains = await db.MainStages.Where(x => x.ProductionLineId == preparedLine.Id).ToListAsync(ct);
        var subStages = await db.SubStages.Include(x => x.MainStage).Where(x => x.MainStage!.ProductionLineId == preparedLine.Id).ToListAsync(ct);
        var mappings = await db.Set<ProductModelStage>().Where(x => x.ProductModelId == preparedProduct.Id).ToListAsync(ct);
        var created = 0;
        var updated = 0;
        foreach (var plan in prepared.Stages)
        {
            var main = mains.SingleOrDefault(x => normalizer.NormalizeLookup(x.Name) == normalizer.NormalizeLookup(plan.Source.MainStageName));
            if (main is null)
            {
                main = new MainStage(Guid.NewGuid(), preparedLine.Id, plan.Source.MainStageName, mains.Count == 0 ? 0 : mains.Max(x => x.SequenceOrder) + 1, false, true, now);
                mains.Add(main); db.Add(main);
            }
            var subStage = subStages.SingleOrDefault(x => StageIdentity(x.MainStage!.Name, x.Name) == StageIdentity(plan.Source.MainStageName, plan.Source.SubStageName));
            if (subStage is null)
            {
                var order = subStages.Where(x => x.MainStageId == main.Id).Select(x => x.DefaultOrder).DefaultIfEmpty(0).Max() + 1;
                subStage = new SubStage(Guid.NewGuid(), main.Id, plan.Source.SubStageName, plan.Code, 0, order, true, now);
                subStages.Add(subStage); db.Add(subStage); created++;
            }
            var mapping = mappings.Single(x => x.SubStageId == subStage.Id);
            if (mapping.PiecePrice != plan.Source.PiecePrice || mapping.StandardSeconds != plan.Source.StandardSeconds)
            {
                mapping.Update(mapping.SubStageId, mapping.StageOrder, plan.Source.PiecePrice, plan.Source.StandardSeconds, mapping.CompensationMode, mapping.IsRequired, mapping.IsActive, mapping.EffectiveFrom, now);
                updated++;
            }
        }
        return (created, updated);
    }

    private async Task<int> ApplyWorkerProjectionAsync(PreparedIntake prepared, Guid actorId, DateTime now, CancellationToken ct)
    {
        var workerIds = prepared.Workers.Where(x => x.Worker is not null).Select(x => x.Worker!.Id).ToArray();
        var trackedWorkers = await db.Workers.Where(x => workerIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var currentSalaries = await db.Set<WorkerSalaryHistory>().Where(x => workerIds.Contains(x.WorkerId) && x.EffectiveTo == null).ToDictionaryAsync(x => x.WorkerId, ct);
        var updated = 0;
        foreach (var plan in prepared.Workers)
        {
            var worker = trackedWorkers[plan.Worker!.Id];
            var changed = !string.Equals(worker.LocalDepartmentName, plan.Source.DepartmentName, StringComparison.Ordinal);
            if (changed) worker.SetLocalDepartmentName(plan.Source.DepartmentName, now);
            currentSalaries.TryGetValue(worker.Id, out var current);
            if (current?.Amount != plan.Source.Salary)
            {
                if (current is not null && now > current.EffectiveFrom) current.Close(now, actorId, now);
                if (plan.Source.Salary.HasValue)
                    db.Add(new WorkerSalaryHistory(Guid.NewGuid(), worker.Id, plan.Source.Salary.Value, "EGP", now, null, "Controlled real-data intake", actorId, actorId, now));
                changed = true;
            }
            if (changed) updated++;
        }
        return updated;
    }

    private async Task<ProductionApplyResult> ApplyProductionDaysAsync(PreparedIntake prepared, ImportBatch batch, Guid actorId, DateTime now, CancellationToken ct)
    {
        var preparedLine = prepared.Line ?? throw new ProductionConflictException("Production line mapping is required before apply.");
        var preparedProduct = prepared.Product ?? throw new ProductionConflictException("Product mapping is required before apply.");
        var mappings = await db.Set<ProductModelStage>().Include(x => x.SubStage).ThenInclude(x => x!.MainStage).ThenInclude(x => x!.ProductionLine).ThenInclude(x => x!.Factory)
            .Where(x => x.ProductModelId == preparedProduct.Id).ToDictionaryAsync(x => x.SubStageId, ct);
        var participatingWorkerIds = prepared.ProductionPlans.Where(plan => plan.Worker is not null).Select(plan => plan.Worker!.Id).Distinct().ToArray();
        var workers = await db.Workers.Where(x => participatingWorkerIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var orders = new Dictionary<DateOnly, ProductionOrder>();
        foreach (var quantity in prepared.Upload.ProductionDayQuantities)
        {
            var order = new ProductionOrder(Guid.NewGuid(), $"INTAKE-{quantity.ProductionDate:yyyyMMdd}-{prepared.IdempotencyKey[..8]}", preparedProduct.Id, preparedLine.Id, quantity.ProductionDate, quantity.LineQuantity, "Controlled real-data intake", actorId, now);
            order.MarkImported(batch.Id, prepared.SourceReference, now);
            db.Add(order);
            orders.Add(quantity.ProductionDate, order);
        }

        var recordsCreated = 0;
        var allocationsCreated = 0;
        foreach (var group in prepared.ProductionPlans.GroupBy(x => new { x.Source.ProductionDate, x.Stage!.Mapping!.SubStageId }))
        {
            var order = orders[group.Key.ProductionDate];
            var mapping = mappings[group.Key.SubStageId];
            var subStage = mapping.SubStage!;
            var mainStage = subStage.MainStage!;
            var line = mainStage.ProductionLine!;
            var factory = line.Factory!;
            var clientRequestId = DeterministicGuid($"{prepared.IdempotencyKey}|{group.Key.ProductionDate:yyyy-MM-dd}|{mapping.Id}");
            var record = new StageProductionRecord(Guid.NewGuid(), order.Id, mapping.Id, order.ProductionDate, order.PlannedQuantity, order.PlannedQuantity, 0m, subStage.Code, subStage.Name, mapping.PiecePrice, mapping.StandardSeconds, mapping.CompensationMode, preparedProduct.Code, preparedProduct.Name, factory.Code, factory.Name, line.LineCode ?? string.Empty, line.Name, mainStage.Name, clientRequestId, "Imported worker allocations", actorId, now);
            var allocations = group.Select(plan => CreateImportedAllocation(plan.Source, workers[plan.Worker!.Id], mapping, order.PlannedQuantity)).ToList();
            record.ReplaceAllocations(allocations);
            record.SetCalculationPreview(decimal.Round(allocations.Sum(x => x.CalculatedEarning), 4, MidpointRounding.AwayFromZero));
            db.Add(record);
            recordsCreated++;
            allocationsCreated += allocations.Count;
        }
        return new ProductionApplyResult(orders.Count, recordsCreated, allocationsCreated, orders.Values.Select(x => x.Id).ToArray());
    }

    private static StageProductionWorkerAllocation CreateImportedAllocation(ParsedProductionRow source, Worker worker, ProductModelStage mapping, decimal lineQuantity)
    {
        var allocation = new StageProductionWorkerAllocation(Guid.NewGuid(), worker.Id, worker.EmployeeCode, worker.FullName, source.Percentage, source.FixedAmount, "Imported worker allocation", null, source.InputQuantity);
        var equivalent = mapping.CompensationMode == CompensationMode.SharedPercentage ? decimal.Round(lineQuantity * source.Percentage!.Value / 100m, 3, MidpointRounding.AwayFromZero) : 0m;
        var earning = mapping.CompensationMode switch
        {
            CompensationMode.SharedPercentage => decimal.Round(equivalent * mapping.PiecePrice, 4, MidpointRounding.AwayFromZero),
            CompensationMode.FixedAmount => decimal.Round(source.FixedAmount!.Value, 4, MidpointRounding.AwayFromZero),
            _ => decimal.Round(lineQuantity * mapping.PiecePrice, 4, MidpointRounding.AwayFromZero)
        };
        allocation.SetCalculatedAmounts(equivalent, earning);
        return allocation;
    }

    private async Task AddAttendanceReadinessIssuesAsync(IReadOnlyCollection<ProductionPlan> plans, CancellationToken ct)
    {
        foreach (var dateGroup in plans.Where(x => x.Worker is not null).GroupBy(x => x.Source.ProductionDate))
        {
            var workerIds = dateGroup.Select(x => x.Worker!.Id).Distinct().ToArray();
            var (start, end) = EgyptUtcRange(dateGroup.Key);
            var records = await db.AttendanceRecords.AsNoTracking().Where(x => workerIds.Contains(x.WorkerId) && x.AttendanceTimeUtc >= start && x.AttendanceTimeUtc < end)
                .OrderByDescending(x => x.AttendanceTimeUtc).ToArrayAsync(ct);
            var byWorker = records.GroupBy(x => x.WorkerId).ToDictionary(x => x.Key, x => x.First());
            foreach (var plan in dateGroup)
            {
                if (string.IsNullOrWhiteSpace(plan.Worker!.AttendanceUserId) && string.IsNullOrWhiteSpace(plan.Worker.BadgeNumber)) plan.Issues.Add(Block("AttendanceWorkerUnmapped", "Worker is not mapped to the read-only attendance source.", plan.Source.SourceRow));
                else if (!byWorker.TryGetValue(plan.Worker.Id, out _)) plan.Issues.Add(Block("MissingAttendance", "No attendance record exists for the production date.", plan.Source.SourceRow));
                else if (byWorker[plan.Worker.Id].AttendanceStatus == AttendanceStatus.Absent) plan.Issues.Add(Block("ConfirmedAbsence", "Worker is confirmed absent for the production date; an authorized override with a reason is required.", plan.Source.SourceRow));
            }
        }
    }

    private async Task EnsureAttendanceAndAssignmentEvidenceAsync(ProductionOrder order, CancellationToken ct)
    {
        var allocations = order.StageProductionRecords.SelectMany(x => x.WorkerAllocations).ToArray();
        var ids = allocations.Select(x => x.WorkerId).Distinct().ToArray();
        var workers = await db.Workers.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var (start, end) = EgyptUtcRange(order.ProductionDate);
        var attendance = await db.AttendanceRecords.AsNoTracking().Where(x => ids.Contains(x.WorkerId) && x.AttendanceTimeUtc >= start && x.AttendanceTimeUtc < end)
            .OrderByDescending(x => x.AttendanceTimeUtc).ToArrayAsync(ct);
        var attendanceByWorker = attendance.GroupBy(x => x.WorkerId).ToDictionary(x => x.Key, x => x.First());
        var assignments = await assignmentEngine.ResolveCurrentAssignmentsAsync(ids, start.AddHours(12), ct);
        if (assignments.IsFailure) throw new ProductionConflictException("Unable to verify historical worker assignments.");
        var subStageIdsByProductStage = await db.Set<ProductModelStage>().AsNoTracking()
            .Where(x => order.StageProductionRecords.Select(record => record.ProductModelStageId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.SubStageId, ct);
        foreach (var record in order.StageProductionRecords)
        foreach (var allocation in record.WorkerAllocations)
        {
            var worker = workers[allocation.WorkerId];
            var attendanceReady = !string.IsNullOrWhiteSpace(worker.AttendanceUserId) || !string.IsNullOrWhiteSpace(worker.BadgeNumber);
            attendanceReady &= attendanceByWorker.TryGetValue(worker.Id, out var attendanceRecord) && attendanceRecord.AttendanceStatus is AttendanceStatus.Present or AttendanceStatus.Late;
            var assignmentReady = assignments.Value!.TryGetValue(worker.Id, out var assignment) && subStageIdsByProductStage.TryGetValue(record.ProductModelStageId, out var subStageId) && assignment.EffectiveSubStageId == subStageId;
            if ((!attendanceReady || !assignmentReady) && string.IsNullOrWhiteSpace(allocation.ManualOverrideReason))
                throw new ProductionConflictException("Attendance, assignment, or an authorized manual-override reason is required for every production participant.");
        }
    }

    private (DateTime StartUtc, DateTime EndUtc) EgyptUtcRange(DateOnly date)
    {
        var localStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var start = TimeZoneInfo.ConvertTimeToUtc(localStart, cairoTimeZoneProvider.TimeZone);
        return (start, TimeZoneInfo.ConvertTimeToUtc(localStart.AddDays(1), cairoTimeZoneProvider.TimeZone));
    }

    private async Task<int> CountOpenReviewIssuesAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken ct)
    {
        var count = 0;
        foreach (var id in orderIds) count += (await GetProductionDayReviewAsync(id, ct)).Issues.Count(x => x.Status == "Open");
        return count;
    }

    private static void ValidateFile(IntakeWorkbookFile file, string name)
    {
        if (file is null || string.IsNullOrWhiteSpace(file.FileName) || file.Content is null || file.Content.Length == 0) throw new ProductionConflictException($"The {name} workbook is required.");
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) throw new ProductionConflictException($"The {name} workbook must be an .xlsx file.");
    }

    private List<ParsedStageRow> ParseStages(IntakeWorkbookFile workbook, List<IntakeIssueDto> issues)
    {
        var result = new List<ParsedStageRow>();
        foreach (var table in ReadWorkbook(workbook, "StageWorkbookSchemaInvalid", issues))
        {
            var mainColumn = table.Column("المرحلة الرئيسية", "القسم", "main stage");
            var stageColumn = table.Column("المرحلة الفرعية", "اسم المرحلة", "stage", "sub stage");
            var priceColumn = table.Column("سعر القطعة", "سعر", "piece price", "price");
            if (stageColumn is null || priceColumn is null) { issues.Add(Block("StageWorkbookSchemaInvalid", "Stages workbook must provide stage name and final piece price columns.")); continue; }
            var codeColumn = table.Column("كود المرحلة", "الكود", "stage code");
            var secondsColumn = table.Column("ثواني القطعة", "standard seconds", "seconds");
            foreach (var row in table.Rows)
            {
                var stageValue = row.Value(stageColumn.Value);
                if (string.IsNullOrWhiteSpace(stageValue)) continue;
                var (main, sub) = SplitStage(mainColumn is null ? null : row.Value(mainColumn.Value), stageValue);
                if (!TryDecimal(row.Value(priceColumn.Value), out var price)) { issues.Add(Block("InvalidPiecePrice", "Final piece price is required and must be numeric.", row.Row)); continue; }
                decimal? seconds = null;
                if (secondsColumn is not null && !string.IsNullOrWhiteSpace(row.Value(secondsColumn.Value)))
                {
                    if (!TryDecimal(row.Value(secondsColumn.Value), out var parsedSeconds)) { issues.Add(Block("InvalidStandardSeconds", "Standard seconds must be numeric when supplied.", row.Row)); continue; }
                    seconds = parsedSeconds;
                }
                result.Add(new ParsedStageRow(row.Row, main, sub, codeColumn is null ? null : EmptyToNull(row.Value(codeColumn.Value)), price, seconds));
            }
        }
        if (result.Count == 0) issues.Add(Block("NoStageRows", "No stage rows were found in the stages workbook."));
        return result;
    }

    private List<ParsedSalaryRow> ParseSalaries(IntakeWorkbookFile workbook, List<IntakeIssueDto> issues)
    {
        var result = new List<ParsedSalaryRow>();
        foreach (var table in ReadWorkbook(workbook, "SalaryWorkbookSchemaInvalid", issues))
        {
            var codeColumn = table.Column("كود الموظف", "كود العامل", "employee code", "code");
            var nameColumn = table.Column("اسم الموظف", "اسم العامل", "name");
            var departmentColumn = table.Column("القسم", "department");
            var salaryColumn = table.Column("الراتب الاساسي", "الراتب", "basic salary", "salary");
            if (codeColumn is null || departmentColumn is null || salaryColumn is null) { issues.Add(Block("SalaryWorkbookSchemaInvalid", "Salary workbook must provide employee code, department, and salary columns.")); continue; }
            foreach (var row in table.Rows)
            {
                var employeeCode = EmptyToNull(row.Value(codeColumn.Value));
                if (employeeCode is null) continue;
                if (!TryDecimal(row.Value(salaryColumn.Value), out var rawSalary) || rawSalary < 0m) { issues.Add(Block("InvalidSalary", "Salary must be a non-negative number.", row.Row)); continue; }
                result.Add(new ParsedSalaryRow(row.Row, employeeCode, nameColumn is null ? string.Empty : row.Value(nameColumn.Value), EmptyToNull(row.Value(departmentColumn.Value)), rawSalary == 0m ? null : rawSalary));
            }
        }
        if (result.Count == 0) issues.Add(Block("NoSalaryRows", "No salary rows were found in the salary workbook."));
        return result;
    }

    private List<ParsedProductionRow> ParseProduction(IntakeWorkbookFile workbook, List<IntakeIssueDto> issues)
    {
        var result = new List<ParsedProductionRow>();
        foreach (var table in ReadWorkbook(workbook, "ProductionWorkbookSchemaInvalid", issues))
        {
            var dateColumn = table.Column("تاريخ الانتاج", "تاريخ الإنتاج", "production date", "date");
            var workerColumn = table.Column("العامل", "الموظف", "worker", "employee");
            var stageColumn = table.Column("المرحلة", "stage");
            var quantityColumn = table.Column("كمية العامل", "كمية الانتاج", "الكمية", "allocation quantity", "quantity");
            if (dateColumn is null || workerColumn is null || stageColumn is null || quantityColumn is null) { issues.Add(Block("ProductionWorkbookSchemaInvalid", "Production workbook must provide production date, worker, stage, and worker allocation quantity columns.")); continue; }
            var percentageColumn = table.Column("النسبة", "percentage");
            var fixedColumn = table.Column("المبلغ", "القيمة الثابتة", "fixed amount");
            foreach (var row in table.Rows)
            {
                if (!TryDate(row.Value(dateColumn.Value), out var date)) { issues.Add(Block("InvalidProductionDate", "Production date is required and must be valid.", row.Row)); continue; }
                var workerCode = ParseWorkerCode(row.Value(workerColumn.Value));
                var stage = EmptyToNull(row.Value(stageColumn.Value));
                if (workerCode is null || stage is null) { issues.Add(Block("InvalidProductionIdentity", "Worker code and stage are required.", row.Row)); continue; }
                decimal? inputQuantity = TryDecimal(row.Value(quantityColumn.Value), out var parsedQuantity) ? parsedQuantity : null;
                decimal? percentage = percentageColumn is not null && TryDecimal(row.Value(percentageColumn.Value), out var parsedPercentage) ? parsedPercentage : null;
                decimal? fixedAmount = fixedColumn is not null && TryDecimal(row.Value(fixedColumn.Value), out var parsedFixed) ? parsedFixed : null;
                result.Add(new ParsedProductionRow(row.Row, date, workerCode, stage, inputQuantity, percentage, fixedAmount));
            }
        }
        if (result.Count == 0) issues.Add(Block("NoProductionRows", "No production worker rows were found in the production workbook."));
        return result;
    }

    private IReadOnlyCollection<WorkbookTable> ReadWorkbook(IntakeWorkbookFile source, string schemaCode, List<IntakeIssueDto> issues)
    {
        var result = new List<WorkbookTable>();
        try
        {
            using var stream = new MemoryStream(source.Content, writable: false);
            using var workbook = new XLWorkbook(stream);
            foreach (var sheet in workbook.Worksheets)
            {
                var range = sheet.RangeUsed();
                if (range is null) continue;
                var firstRow = range.RangeAddress.FirstAddress.RowNumber;
                var lastRow = range.RangeAddress.LastAddress.RowNumber;
                var firstColumn = range.RangeAddress.FirstAddress.ColumnNumber;
                var lastColumn = range.RangeAddress.LastAddress.ColumnNumber;
                var headerRow = Enumerable.Range(firstRow, Math.Min(12, lastRow - firstRow + 1))
                    .OrderByDescending(row => Enumerable.Range(firstColumn, lastColumn - firstColumn + 1).Count(column => !string.IsNullOrWhiteSpace(sheet.Cell(row, column).GetFormattedString())))
                    .First();
                var headers = Enumerable.Range(firstColumn, lastColumn - firstColumn + 1)
                    .Select(column => new { Column = column, Name = sheet.Cell(headerRow, column).GetFormattedString() })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                    .GroupBy(x => normalizer.NormalizeLookup(x.Name)).ToDictionary(x => x.Key, x => x.First().Column);
                var rows = Enumerable.Range(headerRow + 1, lastRow - headerRow)
                    .Select(row => new WorkbookRow(row, Enumerable.Range(firstColumn, lastColumn - firstColumn + 1)
                        .ToDictionary(column => column, column => sheet.Cell(row, column).GetFormattedString())))
                    .ToArray();
                result.Add(new WorkbookTable(headers, rows, normalizer));
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or IOException)
        {
            issues.Add(Block(schemaCode, "Workbook could not be read as a valid .xlsx file."));
        }
        return result;
    }

    private void ValidateSourceStageIdentities(IReadOnlyCollection<ParsedStageRow> stages, List<IntakeIssueDto> issues)
    {
        foreach (var duplicate in stages.GroupBy(x => StageIdentity(x.MainStageName, x.SubStageName)).Where(x => x.Count() > 1))
            foreach (var row in duplicate) issues.Add(Block("DuplicateNormalizedStageIdentity", "Two workbook rows normalize to the same stage identity and require explicit correction.", row.SourceRow));
    }

    private void ValidateDuplicateEmployeeCodes(IReadOnlyCollection<ParsedSalaryRow> workers, List<IntakeIssueDto> issues)
    {
        foreach (var duplicate in workers.GroupBy(x => normalizer.NormalizeEmployeeCode(x.EmployeeCode)).Where(x => x.Count() > 1))
            foreach (var row in duplicate) issues.Add(Block("DuplicateEmployeeCode", "Employee code appears more than once in the salary workbook.", row.SourceRow));
    }

    private void ValidateProductionGroups(IReadOnlyCollection<ProductionPlan> plans, IReadOnlyCollection<ProductionDayQuantityInput> quantities, List<IntakeIssueDto> issues)
    {
        foreach (var duplicate in quantities.GroupBy(x => x.ProductionDate).Where(x => x.Count() > 1)) issues.Add(Block("DuplicateProductionDayQuantity", $"More than one final line quantity was supplied for {duplicate.Key:yyyy-MM-dd}."));
        foreach (var quantity in quantities)
        {
            if (quantity.LineQuantity <= 0m) issues.Add(Block("InvalidLineQuantity", $"Final line quantity for {quantity.ProductionDate:yyyy-MM-dd} must be positive."));
            if (!plans.Any(x => x.Source.ProductionDate == quantity.ProductionDate)) issues.Add(Block("MissingProductionRows", $"No production rows were found for {quantity.ProductionDate:yyyy-MM-dd}."));
        }
        foreach (var day in plans.Select(x => x.Source.ProductionDate).Distinct().Where(day => quantities.All(x => x.ProductionDate != day))) issues.Add(Block("MissingFinalLineQuantity", $"A final line quantity is required for {day:yyyy-MM-dd}."));
        foreach (var group in plans.Where(x => x.Stage?.Mapping is not null && x.Worker is not null).GroupBy(x => new { x.Source.ProductionDate, StageId = x.Stage!.Mapping!.Id, WorkerId = x.Worker!.Id }).Where(x => x.Count() > 1))
            foreach (var row in group) row.Issues.Add(Block("DuplicateWorkerWithinStage", "Worker appears more than once in the same day and stage allocation.", row.Source.SourceRow));
        foreach (var group in plans.Where(x => x.Stage?.Mapping?.CompensationMode == CompensationMode.SharedPercentage).GroupBy(x => new { x.Source.ProductionDate, x.Stage!.Mapping!.Id }))
            if (group.All(x => x.Source.Percentage.HasValue) && group.Sum(x => x.Source.Percentage!.Value) != 100m)
                foreach (var row in group) row.Issues.Add(Block("InvalidSharedPercentageTotal", "Worker percentages for one stage must total exactly 100.", row.Source.SourceRow));
    }

    private IReadOnlyCollection<MissingProductStagePreviewDto> BuildMissingStages(ProductModel? product, IReadOnlyCollection<ProductModelStage> mappings, IReadOnlyCollection<ProductionPlan> plans, IReadOnlyCollection<ProductionDayQuantityInput> quantities)
    {
        if (product is null) return [];
        var result = new List<MissingProductStagePreviewDto>();
        foreach (var day in quantities)
        {
            var present = plans.Where(x => x.Source.ProductionDate == day.ProductionDate && x.Stage?.Mapping is not null).Select(x => x.Stage!.Mapping!.Id).ToHashSet();
            foreach (var mapping in mappings.Where(x => x.IsActive && x.IsRequired && !present.Contains(x.Id)))
                result.Add(new MissingProductStagePreviewDto(day.ProductionDate, mapping.SubStage?.Code ?? string.Empty, mapping.SubStage?.Name ?? string.Empty, Warning, "Required product stage has no workbook allocation for this day and needs manager review."));
        }
        return result;
    }

    private T? MatchExactly<T>(IReadOnlyCollection<T> candidates, string requested, Func<T, string> name, string kind, List<IntakeIssueDto> issues) where T : class
    {
        var matches = candidates.Where(x => normalizer.NormalizeLookup(name(x)) == normalizer.NormalizeLookup(requested)).ToArray();
        if (matches.Length == 1) return matches[0];
        issues.Add(Block(matches.Length == 0 ? $"Missing{kind}" : $"Ambiguous{kind}", matches.Length == 0 ? $"No exact normalized {kind} match exists." : $"More than one exact normalized {kind} match exists."));
        return null;
    }

    private bool MatchesProductionStage(StagePlan stage, string sourceName) =>
        normalizer.NormalizeLookup(stage.Source.SubStageName) == normalizer.NormalizeLookup(sourceName) ||
        normalizer.NormalizeLookup($"{stage.Source.MainStageName} - {stage.Source.SubStageName}") == normalizer.NormalizeLookup(sourceName);

    private string StageIdentity(string main, string sub) => $"{normalizer.NormalizeLookup(main)}\u001F{normalizer.NormalizeLookup(sub)}";
    private static IntakeIssueDto Block(string code, string message, int? row = null) => new(Blocking, code, message, row);
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? ParseWorkerCode(string value) => EmptyToNull(value)?.Split(['-', '–', '—'], 2, StringSplitOptions.TrimEntries)[0];
    private static (string Main, string Sub) SplitStage(string? mainValue, string stageValue)
    {
        if (!string.IsNullOrWhiteSpace(mainValue)) return (mainValue.Trim(), stageValue.Trim());
        var parts = stageValue.Split(" - ", 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : ("غير مصنف", stageValue.Trim());
    }
    private static bool TryDecimal(string value, out decimal result)
    {
        value = ToLatinDigits(value).Replace("٫", ".", StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("ar-EG"), out result);
    }
    private static bool TryDate(string value, out DateOnly date)
    {
        value = ToLatinDigits(value).Trim();
        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date) || DateOnly.TryParse(value, CultureInfo.GetCultureInfo("ar-EG"), DateTimeStyles.AllowWhiteSpaces, out date);
    }
    private static string ToLatinDigits(string value) => value.Replace('٠', '0').Replace('١', '1').Replace('٢', '2').Replace('٣', '3').Replace('٤', '4').Replace('٥', '5').Replace('٦', '6').Replace('٧', '7').Replace('٨', '8').Replace('٩', '9');
    private static string ComputeIdempotencyKey(RealDataIntakeUpload upload)
    {
        using var hash = SHA256.Create();
        void Append(byte[] value) => hash.TransformBlock(value, 0, value.Length, null, 0);
        Append(upload.StagesWorkbook.Content); Append(upload.SalaryWorkbook.Content); Append(upload.ProductionWorkbook.Content);
        Append(Encoding.UTF8.GetBytes(string.Join('|', upload.FactoryName, upload.ProductionLineName, upload.ProductName, string.Join(';', upload.ProductionDayQuantities.OrderBy(x => x.ProductionDate).Select(x => $"{x.ProductionDate:yyyy-MM-dd}:{x.LineQuantity.ToString(CultureInfo.InvariantCulture)}")))));
        hash.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(hash.Hash!).ToLowerInvariant();
    }
    private static Guid DeterministicGuid(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));

    private sealed record ParsedStageRow(int SourceRow, string MainStageName, string SubStageName, string? SourceCode, decimal PiecePrice, decimal? StandardSeconds);
    private sealed record ParsedSalaryRow(int SourceRow, string EmployeeCode, string SourceName, string? DepartmentName, decimal? Salary);
    private sealed record ParsedProductionRow(int SourceRow, DateOnly ProductionDate, string EmployeeCode, string StageName, decimal? InputQuantity, decimal? Percentage, decimal? FixedAmount);
    private sealed record StagePlan(ParsedStageRow Source, SubStage? Existing, ProductModelStage? Mapping, string Code, List<IntakeIssueDto> Issues);
    private sealed record WorkerPlan(ParsedSalaryRow Source, Worker? Worker, decimal? CurrentSalary, List<IntakeIssueDto> Issues);
    private sealed record ProductionPlan(ParsedProductionRow Source, StagePlan? Stage, Worker? Worker, List<IntakeIssueDto> Issues);
    private sealed record PreparedIntake(RealDataIntakeUpload Upload, string IdempotencyKey, string SourceReference, Factory? Factory, ProductionLine? Line, ProductModel? Product, List<StagePlan> Stages, List<WorkerPlan> Workers, List<ProductionPlan> ProductionPlans, IReadOnlyCollection<MissingProductStagePreviewDto> MissingStages, List<IntakeIssueDto> Issues)
    {
        public IReadOnlyCollection<ParsedProductionRow> ProductionRows => ProductionPlans.Select(x => x.Source).ToArray();
        public IReadOnlyCollection<IntakeIssueDto> AllIssues => Issues.Concat(Stages.SelectMany(x => x.Issues)).Concat(Workers.SelectMany(x => x.Issues)).Concat(ProductionPlans.SelectMany(x => x.Issues)).ToArray();
        public bool HasBlockingIssues => AllIssues.Any(x => x.Severity == Blocking);
    }
    private sealed record ProductionApplyResult(int DaysCreated, int RecordsCreated, int AllocationsCreated, IReadOnlyCollection<Guid> OrderIds);
    private sealed record WorkbookRow(int Row, IReadOnlyDictionary<int, string> Values)
    {
        public string Value(int column) => Values.TryGetValue(column, out var value) ? value : string.Empty;
    }
    private sealed class WorkbookTable(IReadOnlyDictionary<string, int> headers, IReadOnlyCollection<WorkbookRow> rows, IImportNormalizationService normalization)
    {
        public IReadOnlyCollection<WorkbookRow> Rows { get; } = rows;
        public int? Column(params string[] aliases)
        {
            foreach (var alias in aliases)
            {
                var normalized = normalization.NormalizeLookup(alias);
                if (headers.TryGetValue(normalized, out var exact)) return exact;
            }
            return null;
        }
    }
}
