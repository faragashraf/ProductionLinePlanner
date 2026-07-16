using System.Data;
using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.Bootstrap;

/// <summary>
/// Development-only reconciliation for the first pilot's master data. It deliberately
/// does not accept production rows, create workers, or communicate with ZKTime.
/// </summary>
public sealed class PilotMasterDataBootstrapService(
    AppDbContext db,
    IImportNormalizationService normalizer,
    IAuditEngine audit) : IPilotMasterDataBootstrapService
{
    private const string FactoryName = "المصنع الرئيسي";
    private const string ProductionLineName = "خط الخياطه";
    private const string ProductName = "جرومان";
    private const string Blocking = "blocking";
    private const string Warning = "warning";
    private const CompensationMode ProvisionalPilotCompensationMode = CompensationMode.SharedPercentage;

    public async Task<PilotMasterDataBootstrapPreviewDto> PreviewAsync(
        PilotMasterDataBootstrapInput input,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(input, cancellationToken);
        return ToPreview(prepared);
    }

    public async Task<PilotMasterDataBootstrapApplyResultDto> ApplyAsync(
        PilotMasterDataBootstrapInput input,
        Guid actorUserId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException("Bootstrap apply requires an explicit confirmation flag.");
        }

        await EnsureActiveSuperAdminAsync(actorUserId, cancellationToken);

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        try
        {
            // Re-read under the transaction so the dry run cannot become a stale apply plan.
            var prepared = await PrepareAsync(input, cancellationToken);
            var preview = ToPreview(prepared);
            if (!preview.CanApply)
            {
                throw new InvalidOperationException("Bootstrap apply is blocked by the dry-run validation result.");
            }

            if (IsAlreadyCurrent(preview))
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return new PilotMasterDataBootstrapApplyResultDto(true, preview);
            }

            var now = DateTime.UtcNow;
            var factory = await ApplyFactoryAsync(prepared, now, cancellationToken);
            var line = await ApplyProductionLineAsync(prepared, factory, now, cancellationToken);
            var product = await ApplyProductAsync(prepared, now, cancellationToken);
            await ApplyStagesAndMappingsAsync(prepared, line, product, now, cancellationToken);
            await ApplyWorkerProjectionAsync(prepared, actorUserId, now, cancellationToken);

            await audit.RecordAsync(
                actorUserId,
                AuditActionType.Create,
                "PilotMasterDataBootstrap",
                product.Id.ToString(),
                after: new
                {
                    Factory = preview.FactoryAction,
                    ProductionLine = preview.ProductionLineAction,
                    Product = preview.ProductAction,
                    preview.SourceStageRows,
                    preview.SourceWorkerRows,
                    preview.StagesCreated,
                    preview.StagesUpdated,
                    preview.ProductStageMappingsCreated,
                    preview.ProductStageMappingsUpdated,
                    preview.ProvisionalCompensationMappingsForReview,
                    preview.DepartmentsUpdated,
                    preview.WorkersMatched,
                    preview.WorkersUnmatched,
                    preview.SalariesUpdated,
                    preview.SalariesSetNull
                },
                requestMeta: "PilotMasterDataBootstrap",
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new PilotMasterDataBootstrapApplyResultDto(false, preview);
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<PilotMasterDataBootstrapVerificationDto> VerifyAsync(
        PilotMasterDataBootstrapInput input,
        CancellationToken cancellationToken = default)
    {
        // Parsing here is read-only and keeps verification tied to the authoritative source,
        // rather than relying on a copied workbook or a persisted import artifact.
        var prepared = await PrepareAsync(input, cancellationToken);
        var factories = await db.Factories.AsNoTracking().ToArrayAsync(cancellationToken);
        var factoryMatches = Match(factories, FactoryName, x => x.Name);
        var targetFactory = factoryMatches.Length == 1 ? factoryMatches[0] : null;
        var lines = targetFactory is null
            ? []
            : await db.ProductionLines.AsNoTracking().Where(x => x.FactoryId == targetFactory.Id).ToArrayAsync(cancellationToken);
        var lineMatches = Match(lines, ProductionLineName, x => x.Name);
        var targetLine = lineMatches.Length == 1 ? lineMatches[0] : null;
        var products = await db.ProductModels.AsNoTracking().ToArrayAsync(cancellationToken);
        var productMatches = Match(products, ProductName, x => x.Name);
        var targetProduct = productMatches.Length == 1 ? productMatches[0] : null;

        var stages = targetLine is null
            ? []
            : await db.SubStages.AsNoTracking().Include(x => x.MainStage)
                .Where(x => x.MainStage!.ProductionLineId == targetLine.Id)
                .ToArrayAsync(cancellationToken);
        var mappings = targetProduct is null
            ? []
            : await db.ProductModelStages.AsNoTracking().Where(x => x.ProductModelId == targetProduct.Id).ToArrayAsync(cancellationToken);

        var expectedCodes = Enumerable.Range(1, prepared.SourceStages.Count).Select(x => $"STG{x:000}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actualCodes = stages.Select(x => x.Code).ToArray();
        var stageCodesAreUniqueAndStable = actualCodes.Length == expectedCodes.Count &&
            actualCodes.Distinct(StringComparer.OrdinalIgnoreCase).Count() == expectedCodes.Count &&
            actualCodes.All(expectedCodes.Contains);
        var stageIdentitiesAreUnique = stages
            .GroupBy(x => StageIdentity(x.MainStage!.Name, x.Name))
            .All(x => x.Count() == 1);
        var stageByIdentity = stages.ToDictionary(x => StageIdentity(x.MainStage!.Name, x.Name));
        var mappingBySubStage = mappings.GroupBy(x => x.SubStageId).ToDictionary(x => x.Key, x => x.ToArray());
        var sourcePricesMatch = prepared.SourceStages.All(source =>
            stageByIdentity.TryGetValue(StageIdentity(source.MainStageName, source.SubStageName), out var stage) &&
            mappingBySubStage.TryGetValue(stage.Id, out var stageMappings) && stageMappings.Length == 1 &&
            stageMappings[0].PiecePrice == source.PiecePrice);
        var mappingsWithMissingSeconds = prepared.SourceStages.Count(source =>
            source.StandardSeconds is null &&
            stageByIdentity.TryGetValue(StageIdentity(source.MainStageName, source.SubStageName), out var stage) &&
            mappingBySubStage.TryGetValue(stage.Id, out var stageMappings) && stageMappings.Length == 1 &&
            stageMappings[0].StandardSeconds is null);

        var matchedWorkerIds = prepared.Workers.Where(x => x.Worker is not null).Select(x => x.Worker!.Id).Distinct().ToArray();
        var currentSalaryWorkerIds = await db.WorkerSalaryHistories.AsNoTracking()
            .Where(x => matchedWorkerIds.Contains(x.WorkerId) && x.EffectiveTo == null)
            .Select(x => x.WorkerId)
            .ToArrayAsync(cancellationToken);
        var salaryZeroStoredAsNull = prepared.Workers.Count(x => x.Worker is not null && x.Source.Salary is null && !currentSalaryWorkerIds.Contains(x.Worker.Id));
        var activeSuperAdminCount = await db.AppUsers.AsNoTracking().Include(x => x.Roles)
            .CountAsync(x => x.IsActive && x.Roles.Any(role => role.IsActive && role.Role == UserRole.SuperAdmin), cancellationToken);

        return new PilotMasterDataBootstrapVerificationDto
        {
            ActiveSuperAdminCount = activeSuperAdminCount,
            TargetFactoryCount = factoryMatches.Length,
            TargetProductionLineCount = lineMatches.Length,
            TargetProductCount = productMatches.Length,
            TargetStageCount = stages.Length,
            TargetProductStageMappingCount = mappings.Length,
            StageCodesAreUniqueAndStable = stageCodesAreUniqueAndStable,
            StageIdentitiesAreUnique = stageIdentitiesAreUnique,
            SourceRowsWithMissingSeconds = prepared.SourceStages.Count(x => x.StandardSeconds is null),
            MappingsWithMissingSeconds = mappingsWithMissingSeconds,
            SourcePricesMatch = sourcePricesMatch,
            WorkersMatchedByEmployeeCode = prepared.Workers.Count(x => x.Worker is not null),
            WorkersUnmatchedByEmployeeCode = prepared.Workers.Count(x => x.Worker is null),
            MatchedSalaryZeroRowsStoredAsNull = salaryZeroStoredAsNull,
            ProductionOrders = await db.Set<ProductionOrder>().AsNoTracking().CountAsync(cancellationToken),
            SelectionChainAvailable = factoryMatches.Length == 1 && lineMatches.Length == 1 && productMatches.Length == 1 &&
                stages.Length == prepared.SourceStages.Count && mappings.Length == prepared.SourceStages.Count &&
                mappings.All(x => x.IsActive),
            ProvisionalCompensationMappingsForReview = mappings.Count(x => x.CompensationMode == ProvisionalPilotCompensationMode),
            UnmatchedEmployeeCodes = prepared.Workers.Where(x => x.Worker is null).Select(x => x.Source.EmployeeCode)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private async Task<PreparedBootstrap> PrepareAsync(PilotMasterDataBootstrapInput input, CancellationToken ct)
    {
        var issues = new List<PilotBootstrapIssueDto>();
        if (!input.ProductionWorkbookVerified)
        {
            issues.Add(Block("ProductionWorkbookUnavailable", "The deferred production workbook must be present before this controlled bootstrap can run."));
        }

        var sourceStages = ParseStages(input.StagesWorkbook, issues);
        var sourceWorkers = ParseSalaryRows(input.SalaryWorkbook, issues);
        ValidateSourceStages(sourceStages, issues);
        ValidateSourceWorkers(sourceWorkers, issues);

        var factories = await db.Factories.AsNoTracking().ToArrayAsync(ct);
        var factoryMatches = Match(factories, FactoryName, x => x.Name);
        AddSingletonIssue(factoryMatches.Length, "Factory", issues);
        var existingFactory = factoryMatches.Length == 1 ? factoryMatches[0] : null;
        if (existingFactory is null && factories.Any(x => string.Equals(x.Code, "MAIN", StringComparison.OrdinalIgnoreCase)))
            issues.Add(Block("FactoryCodeConflict", "The deterministic factory code is already assigned to a different factory."));

        var lines = existingFactory is null
            ? Array.Empty<ProductionLine>()
            : await db.ProductionLines.AsNoTracking().Where(x => x.FactoryId == existingFactory.Id).ToArrayAsync(ct);
        var lineMatches = Match(lines, ProductionLineName, x => x.Name);
        AddSingletonIssue(lineMatches.Length, "ProductionLine", issues);
        var existingLine = lineMatches.Length == 1 ? lineMatches[0] : null;
        if (existingFactory is not null && existingLine is null && lines.Any(x => string.Equals(x.LineCode, "SEW", StringComparison.OrdinalIgnoreCase)))
            issues.Add(Block("ProductionLineCodeConflict", "The deterministic production-line code is already assigned within the matching factory."));

        var products = await db.ProductModels.AsNoTracking().ToArrayAsync(ct);
        var productMatches = Match(products, ProductName, x => x.Name);
        AddSingletonIssue(productMatches.Length, "Product", issues);
        var existingProduct = productMatches.Length == 1 ? productMatches[0] : null;
        if (existingProduct is null && products.Any(x => string.Equals(x.Code, "GEROMAN", StringComparison.OrdinalIgnoreCase)))
            issues.Add(Block("ProductCodeConflict", "The deterministic product code is already assigned to a different product."));

        if (existingFactory is not null && !existingFactory.IsActive)
            issues.Add(Block("InactiveFactory", "The matching factory is inactive and must be reviewed before bootstrap."));
        if (existingLine is not null && !existingLine.IsActive)
            issues.Add(Block("InactiveProductionLine", "The matching production line is inactive and must be reviewed before bootstrap."));
        if (existingProduct is not null && !existingProduct.IsActive)
            issues.Add(Block("InactiveProduct", "The matching product is inactive and must be reviewed before bootstrap."));

        var existingSubStages = existingLine is null
            ? Array.Empty<SubStage>()
            : await db.SubStages.AsNoTracking()
                .Include(x => x.MainStage)
                .Where(x => x.MainStage!.ProductionLineId == existingLine.Id)
                .ToArrayAsync(ct);
        var allSubStages = await db.SubStages.AsNoTracking().ToArrayAsync(ct);
        var existingMappings = existingProduct is null
            ? Array.Empty<ProductModelStage>()
            : await db.ProductModelStages.AsNoTracking().Where(x => x.ProductModelId == existingProduct.Id).ToArrayAsync(ct);

        var stages = BuildStagePlans(sourceStages, existingSubStages, allSubStages, existingMappings, input.ExplicitCompensationMode, issues);
        var unexpectedProductStageMappings = ValidateExistingProductMappings(existingMappings, stages, issues);

        var workers = await db.Workers.AsNoTracking().ToArrayAsync(ct);
        var currentSalaries = await db.WorkerSalaryHistories.AsNoTracking()
            .Where(x => x.EffectiveTo == null)
            .ToArrayAsync(ct);
        var workerPlans = BuildWorkerPlans(sourceWorkers, workers, currentSalaries, issues);

        return new PreparedBootstrap(
            input, sourceStages, sourceWorkers, existingFactory, existingLine, existingProduct, stages, workerPlans,
            unexpectedProductStageMappings,
            existingMappings.Where(x => x.IsActive).Select(x => x.CompensationMode.ToString()).Distinct(StringComparer.Ordinal).OrderBy(x => x).ToArray(),
            issues);
    }

    private IReadOnlyCollection<StagePlan> BuildStagePlans(
        IReadOnlyCollection<SourceStage> sourceStages,
        IReadOnlyCollection<SubStage> existingSubStages,
        IReadOnlyCollection<SubStage> allSubStages,
        IReadOnlyCollection<ProductModelStage> existingMappings,
        CompensationMode? explicitCompensationMode,
        List<PilotBootstrapIssueDto> issues)
    {
        var plans = new List<StagePlan>();
        var existingByIdentity = existingSubStages
            .GroupBy(x => StageIdentity(x.MainStage!.Name, x.Name))
            .ToDictionary(x => x.Key, x => x.ToArray());
        var mappingsByStage = existingMappings.GroupBy(x => x.SubStageId).ToDictionary(x => x.Key, x => x.ToArray());
        var globalCodes = allSubStages.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var plannedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var generatedNumber = 4;

        foreach (var source in sourceStages.OrderBy(x => x.SourceRow))
        {
            var rowIssues = new List<PilotBootstrapIssueDto>();
            var identity = StageIdentity(source.MainStageName, source.SubStageName);
            var candidates = existingByIdentity.GetValueOrDefault(identity, []);
            if (candidates.Length > 1)
            {
                rowIssues.Add(Block("AmbiguousStageIdentity", "More than one existing stage has this normalized hierarchy and name."));
            }

            var existing = candidates.Length == 1 ? candidates[0] : null;
            string code;
            var generated = false;
            if (!string.IsNullOrWhiteSpace(source.SourceCode))
            {
                code = source.SourceCode.Trim();
                if (existing is not null && !string.Equals(existing.Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    rowIssues.Add(Block("PreservedStageCodeConflict", "The existing matched stage code differs from the preserved workbook code."));
                }
            }
            else if (existing is not null)
            {
                code = existing.Code;
            }
            else
            {
                code = $"STG{generatedNumber++:000}";
                generated = true;
            }

            var codeOwners = allSubStages.Where(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (codeOwners.Length > 0 && (existing is null || codeOwners.Any(x => x.Id != existing.Id)))
            {
                rowIssues.Add(Block("DuplicateStageCode", "The generated or preserved stage code belongs to a different stage."));
            }
            if (!plannedCodes.Add(code) && (existing is null || !string.Equals(existing.Code, code, StringComparison.OrdinalIgnoreCase)))
            {
                rowIssues.Add(Block("DuplicateStageCodeInWorkbook", "The workbook produces the same stage code more than once."));
            }
            if (globalCodes.Contains(code) && existing is null && codeOwners.Length == 0)
            {
                rowIssues.Add(Block("DuplicateStageCode", "The generated or preserved stage code is already in use."));
            }

            var mappingCandidates = existing is null ? [] : mappingsByStage.GetValueOrDefault(existing.Id, []);
            if (mappingCandidates.Length > 1)
            {
                rowIssues.Add(Block("AmbiguousProductStageMapping", "More than one product-stage mapping exists for this stage."));
            }
            var mapping = mappingCandidates.Length == 1 ? mappingCandidates[0] : null;
            // The source workbooks do not define a compensation mode. New mappings use the
            // existing SharedPercentage mode as a provisional, explicitly reportable pilot default.
            var usesProvisionalCompensation = mapping is null && !explicitCompensationMode.HasValue;
            plans.Add(new StagePlan(source, existing, mapping, code, generated, usesProvisionalCompensation, rowIssues));
        }
        return plans;
    }

    private int ValidateExistingProductMappings(
        IReadOnlyCollection<ProductModelStage> existingMappings,
        IReadOnlyCollection<StagePlan> stagePlans,
        List<PilotBootstrapIssueDto> issues)
    {
        var allowedExistingIds = stagePlans.Where(x => x.Existing is not null).Select(x => x.Existing!.Id).ToHashSet();
        var unexpectedCount = existingMappings.Count(x => x.IsActive && !allowedExistingIds.Contains(x.SubStageId));
        if (unexpectedCount > 0)
        {
            issues.Add(Block("UnexpectedProductStageMapping", $"{unexpectedCount} active mappings on the matching product are outside the authoritative 67 target stages."));
        }
        return unexpectedCount;
    }

    private IReadOnlyCollection<WorkerPlan> BuildWorkerPlans(
        IReadOnlyCollection<SourceWorker> sourceWorkers,
        IReadOnlyCollection<Worker> existingWorkers,
        IReadOnlyCollection<WorkerSalaryHistory> currentSalaries,
        List<PilotBootstrapIssueDto> issues)
    {
        var workersByCode = existingWorkers
            .GroupBy(x => normalizer.NormalizeEmployeeCode(x.EmployeeCode))
            .ToDictionary(x => x.Key, x => x.ToArray());

        var plans = new List<WorkerPlan>();
        foreach (var source in sourceWorkers)
        {
            var matches = workersByCode.GetValueOrDefault(normalizer.NormalizeEmployeeCode(source.EmployeeCode), []);
            var rowIssues = new List<PilotBootstrapIssueDto>();
            if (matches.Length == 0)
            {
                rowIssues.Add(Warn("UnmatchedEmployeeCode", "No application or local ZKTime projection worker matches this employee code; the row will be skipped."));
            }
            else if (matches.Length > 1)
            {
                rowIssues.Add(Block("DuplicateApplicationEmployeeCode", "More than one application worker has this employee code."));
            }

            var worker = matches.Length == 1 ? matches[0] : null;
            var salaryRows = worker is null
                ? []
                : currentSalaries.Where(x => x.WorkerId == worker.Id).OrderByDescending(x => x.EffectiveFrom).ToArray();
            if (salaryRows.Length > 1)
            {
                rowIssues.Add(Block("DuplicateCurrentSalary", "The matched worker has more than one current salary history row."));
            }

            plans.Add(new WorkerPlan(source, worker, salaryRows.Length == 1 ? salaryRows[0] : null, rowIssues));
        }
        return plans;
    }

    private async Task<Factory> ApplyFactoryAsync(PreparedBootstrap prepared, DateTime now, CancellationToken ct)
    {
        if (prepared.Factory is not null)
        {
            return await db.Factories.SingleAsync(x => x.Id == prepared.Factory.Id, ct);
        }

        var factory = new Factory(Guid.NewGuid(), FactoryName, "MAIN", createdAtUtc: now);
        db.Factories.Add(factory);
        return factory;
    }

    private async Task<ProductionLine> ApplyProductionLineAsync(PreparedBootstrap prepared, Factory factory, DateTime now, CancellationToken ct)
    {
        if (prepared.ProductionLine is not null)
        {
            return await db.ProductionLines.SingleAsync(x => x.Id == prepared.ProductionLine.Id, ct);
        }

        var lastOrder = await db.ProductionLines.Where(x => x.FactoryId == factory.Id)
            .Select(x => (int?)x.SequenceOrder).MaxAsync(ct) ?? -1;
        var line = new ProductionLine(Guid.NewGuid(), factory.Id, ProductionLineName, lastOrder + 1, "SEW", true, now);
        db.ProductionLines.Add(line);
        return line;
    }

    private async Task<ProductModel> ApplyProductAsync(PreparedBootstrap prepared, DateTime now, CancellationToken ct)
    {
        if (prepared.Product is not null)
        {
            return await db.ProductModels.SingleAsync(x => x.Id == prepared.Product.Id, ct);
        }

        var product = new ProductModel(Guid.NewGuid(), "GEROMAN", ProductName, createdAtUtc: now);
        db.ProductModels.Add(product);
        return product;
    }

    private async Task ApplyStagesAndMappingsAsync(PreparedBootstrap prepared, ProductionLine line, ProductModel product, DateTime now, CancellationToken ct)
    {
        var mains = await db.MainStages.Where(x => x.ProductionLineId == line.Id).ToListAsync(ct);
        var subStages = await db.SubStages.Include(x => x.MainStage).Where(x => x.MainStage!.ProductionLineId == line.Id).ToListAsync(ct);
        var mappings = await db.ProductModelStages.Where(x => x.ProductModelId == product.Id).ToListAsync(ct);

        foreach (var plan in prepared.Stages.OrderBy(x => x.Source.SourceRow))
        {
            var mainMatches = mains.Where(x => normalizer.NormalizeLookup(x.Name) == normalizer.NormalizeLookup(plan.Source.MainStageName)).ToArray();
            if (mainMatches.Length > 1)
            {
                throw new InvalidOperationException("More than one tracked main stage matches a bootstrap stage.");
            }
            var main = mainMatches.SingleOrDefault();
            if (main is null)
            {
                var nextOrder = mains.Count == 0 ? 0 : mains.Max(x => x.SequenceOrder) + 1;
                main = new MainStage(Guid.NewGuid(), line.Id, plan.Source.MainStageName, nextOrder, false, true, now);
                mains.Add(main);
                db.MainStages.Add(main);
            }

            var identity = StageIdentity(plan.Source.MainStageName, plan.Source.SubStageName);
            var stageMatches = subStages.Where(x => StageIdentity(x.MainStage!.Name, x.Name) == identity).ToArray();
            if (stageMatches.Length > 1)
            {
                throw new InvalidOperationException("More than one tracked sub-stage matches a bootstrap stage.");
            }
            var subStage = stageMatches.SingleOrDefault();
            if (subStage is null)
            {
                var nextOrder = subStages.Where(x => x.MainStageId == main.Id).Select(x => x.DefaultOrder).DefaultIfEmpty(0).Max() + 1;
                subStage = new SubStage(Guid.NewGuid(), main.Id, plan.Source.SubStageName, plan.Code, 0, nextOrder, true, now);
                subStages.Add(subStage);
                db.SubStages.Add(subStage);
            }

            var mappingMatches = mappings.Where(x => x.SubStageId == subStage.Id).ToArray();
            if (mappingMatches.Length > 1)
            {
                throw new InvalidOperationException("More than one tracked product-stage mapping matches a bootstrap stage.");
            }
            var mapping = mappingMatches.SingleOrDefault();
            if (mapping is null)
            {
                var compensationMode = prepared.Input.ExplicitCompensationMode ?? ProvisionalPilotCompensationMode;
                mapping = new ProductModelStage(
                    Guid.NewGuid(), product.Id, subStage.Id, plan.Source.SourceRow,
                    plan.Source.PiecePrice, plan.Source.StandardSeconds, compensationMode,
                    isRequired: true, isActive: true, createdAtUtc: now);
                mappings.Add(mapping);
                db.ProductModelStages.Add(mapping);
            }
            else if (mapping.PiecePrice != plan.Source.PiecePrice || mapping.StandardSeconds != plan.Source.StandardSeconds || mapping.StageOrder != plan.Source.SourceRow)
            {
                mapping.Update(subStage.Id, plan.Source.SourceRow, plan.Source.PiecePrice, plan.Source.StandardSeconds,
                    mapping.CompensationMode, mapping.IsRequired, mapping.IsActive, mapping.EffectiveFrom, now);
            }
        }
    }

    private async Task ApplyWorkerProjectionAsync(PreparedBootstrap prepared, Guid actorId, DateTime now, CancellationToken ct)
    {
        var workerIds = prepared.Workers.Where(x => x.Worker is not null).Select(x => x.Worker!.Id).Distinct().ToArray();
        var workers = await db.Workers.Where(x => workerIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var currentSalaries = await db.WorkerSalaryHistories.Where(x => workerIds.Contains(x.WorkerId) && x.EffectiveTo == null)
            .ToDictionaryAsync(x => x.WorkerId, ct);

        foreach (var plan in prepared.Workers)
        {
            if (plan.Worker is null)
            {
                continue;
            }
            var worker = workers[plan.Worker!.Id];
            if (!string.Equals(worker.LocalDepartmentName, plan.Source.DepartmentName, StringComparison.Ordinal))
            {
                worker.SetLocalDepartmentName(plan.Source.DepartmentName, now);
            }

            currentSalaries.TryGetValue(worker.Id, out var current);
            var currentAmount = current?.Amount;
            if (currentAmount == plan.Source.Salary)
            {
                continue;
            }
            if (current is not null)
            {
                if (current.EffectiveFrom < now)
                {
                    current.Close(now, actorId, now);
                }
                else
                {
                    // A same-instant current row has no historical interval to preserve.
                    db.WorkerSalaryHistories.Remove(current);
                }
            }
            if (plan.Source.Salary.HasValue)
            {
                db.WorkerSalaryHistories.Add(new WorkerSalaryHistory(
                    Guid.NewGuid(), worker.Id, plan.Source.Salary.Value, "EGP", now, null,
                    "Pilot master-data bootstrap", actorId, actorId, now));
            }
        }
    }

    private async Task EnsureActiveSuperAdminAsync(Guid actorUserId, CancellationToken ct)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("A Super Admin actor is required for bootstrap apply.");
        }

        var user = await db.AppUsers.AsNoTracking().Include(x => x.Roles)
            .SingleOrDefaultAsync(x => x.Id == actorUserId, ct);
        if (user is null || !user.IsActive || !user.Roles.Any(x => x.IsActive && x.Role == UserRole.SuperAdmin))
        {
            throw new UnauthorizedAccessException("Bootstrap apply requires an active Super Admin actor.");
        }
    }

    private PilotMasterDataBootstrapPreviewDto ToPreview(PreparedBootstrap prepared)
    {
        var allIssues = prepared.AllIssues.ToArray();
        var stagePlans = prepared.Stages;
        var workerPlans = prepared.Workers;
        var stageCreates = stagePlans.Count(x => x.Existing is null && !HasBlockingIssue(x.Issues));
        var stageUnchanged = stagePlans.Count(x => x.Existing is not null && !HasBlockingIssue(x.Issues));
        var mappingCreates = stagePlans.Count(x => x.Mapping is null && !HasBlockingIssue(x.Issues));
        var mappingUpdates = stagePlans.Count(x => x.Mapping is not null && !HasBlockingIssue(x.Issues) &&
            (x.Mapping.PiecePrice != x.Source.PiecePrice || x.Mapping.StandardSeconds != x.Source.StandardSeconds || x.Mapping.StageOrder != x.Source.SourceRow));
        var mappingUnchanged = stagePlans.Count(x => x.Mapping is not null && !HasBlockingIssue(x.Issues)) - mappingUpdates;
        var validWorkers = workerPlans.Where(x => x.Worker is not null && !HasBlockingIssue(x.Issues)).ToArray();
        var departmentsUpdated = validWorkers.Count(x => !string.Equals(x.Worker!.LocalDepartmentName, x.Source.DepartmentName, StringComparison.Ordinal));
        var salariesUpdated = validWorkers.Count(x => x.CurrentSalary?.Amount != x.Source.Salary);
        var salariesSetNull = workerPlans.Count(x => x.Source.Salary is null);

        return new PilotMasterDataBootstrapPreviewDto
        {
            CanApply = !allIssues.Any(x => x.Severity == Blocking),
            FactoryAction = SingletonAction(prepared.Factory, allIssues, "Factory"),
            ProductionLineAction = SingletonAction(prepared.ProductionLine, allIssues, "ProductionLine"),
            ProductAction = SingletonAction(prepared.Product, allIssues, "Product"),
            SourceStageRows = prepared.SourceStages.Count,
            SourceWorkerRows = prepared.SourceWorkers.Count,
            SourceDepartmentCount = prepared.SourceWorkers.Select(x => normalizer.NormalizeLookup(x.DepartmentName ?? string.Empty)).Where(x => x.Length > 0).Distinct().Count(),
            StagesCreated = stageCreates,
            StagesUpdated = 0,
            StagesUnchanged = stageUnchanged,
            GeneratedCodes = stagePlans.Where(x => x.Generated && x.Issues.Count == 0).Select(x => x.Code).ToArray(),
            ProductStageMappingsCreated = mappingCreates,
            ProductStageMappingsUpdated = mappingUpdates,
            ProductStageMappingsUnchanged = mappingUnchanged,
            ProvisionalCompensationMappingsForReview = stagePlans.Count(x => x.Mapping is null && x.UsesProvisionalCompensation && !HasBlockingIssue(x.Issues)),
            ExistingProductStageMappingsOutsideTarget = prepared.UnexpectedProductStageMappings,
            ExistingProductCompensationModes = prepared.ExistingProductCompensationModes,
            DepartmentsUpdated = departmentsUpdated,
            WorkersMatched = workerPlans.Count(x => x.Worker is not null),
            WorkersUnmatched = workerPlans.Count(x => x.Worker is null),
            SalariesUpdated = salariesUpdated,
            SalariesSetNull = salariesSetNull,
            UnmatchedEmployeeCodes = workerPlans.Where(x => x.Worker is null).Select(x => x.Source.EmployeeCode)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            Issues = allIssues
        };
    }

    private static bool IsAlreadyCurrent(PilotMasterDataBootstrapPreviewDto preview) =>
        preview.FactoryAction == "reused" && preview.ProductionLineAction == "reused" && preview.ProductAction == "reused" &&
        preview.StagesCreated == 0 && preview.StagesUpdated == 0 &&
        preview.ProductStageMappingsCreated == 0 && preview.ProductStageMappingsUpdated == 0 &&
        preview.DepartmentsUpdated == 0 && preview.SalariesUpdated == 0;

    private string SingletonAction<T>(T? entity, IReadOnlyCollection<PilotBootstrapIssueDto> issues, string kind) where T : class
    {
        if (issues.Any(x => string.Equals(x.Code, $"Missing{kind}", StringComparison.Ordinal) ||
                            string.Equals(x.Code, $"Ambiguous{kind}", StringComparison.Ordinal) ||
                            string.Equals(x.Code, $"Inactive{kind}", StringComparison.Ordinal) ||
                            string.Equals(x.Code, $"{kind}CodeConflict", StringComparison.Ordinal))) return "blocked";
        return entity is null ? "created" : "reused";
    }

    private void ValidateSourceStages(IReadOnlyCollection<SourceStage> sourceStages, List<PilotBootstrapIssueDto> issues)
    {
        if (sourceStages.Count != 67)
        {
            issues.Add(Block("UnexpectedStageCount", "The stages workbook must contain exactly 67 authoritative stage rows."));
        }
        foreach (var duplicate in sourceStages.GroupBy(x => StageIdentity(x.MainStageName, x.SubStageName)).Where(x => x.Count() > 1))
        {
            issues.Add(Block("DuplicateNormalizedStageIdentity", "Two workbook stage rows normalize to the same hierarchy and name."));
        }
        foreach (var duplicate in sourceStages.Where(x => !string.IsNullOrWhiteSpace(x.SourceCode)).GroupBy(x => x.SourceCode!, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
        {
            issues.Add(Block("DuplicatePreservedStageCode", "A preserved stage code appears more than once in the stages workbook."));
        }
    }

    private void ValidateSourceWorkers(IReadOnlyCollection<SourceWorker> sourceWorkers, List<PilotBootstrapIssueDto> issues)
    {
        if (sourceWorkers.Count == 0)
        {
            issues.Add(Block("NoSalaryRows", "No worker salary and department rows were found."));
        }
        foreach (var duplicate in sourceWorkers.GroupBy(x => normalizer.NormalizeEmployeeCode(x.EmployeeCode)).Where(x => x.Count() > 1))
        {
            issues.Add(Block("DuplicateEmployeeCode", "An employee code appears more than once in the salary workbook."));
        }
    }

    private static void AddSingletonIssue(int count, string kind, List<PilotBootstrapIssueDto> issues)
    {
        if (count == 0) return;
        if (count > 1) issues.Add(Block($"Ambiguous{kind}", $"More than one normalized {kind} record matches the controlled bootstrap target."));
    }

    private T[] Match<T>(IReadOnlyCollection<T> candidates, string value, Func<T, string> selector) where T : class =>
        candidates.Where(x => normalizer.NormalizeLookup(selector(x)) == normalizer.NormalizeLookup(value)).ToArray();

    private IReadOnlyCollection<SourceStage> ParseStages(byte[] content, List<PilotBootstrapIssueDto> issues)
    {
        var result = new List<SourceStage>();
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheets.FirstOrDefault();
            if (worksheet is null || worksheet.RangeUsed() is not { } range)
            {
                issues.Add(Block("StageWorkbookSchemaInvalid", "The stages workbook has no used worksheet range."));
                return result;
            }

            var headers = HeaderMap(worksheet, range.RangeAddress.FirstAddress.RowNumber, range.RangeAddress.FirstAddress.ColumnNumber, range.RangeAddress.LastAddress.ColumnNumber);
            if (!headers.TryGetValue(normalizer.NormalizeLookup("المرحلة الرئيسية"), out var mainColumn) ||
                !headers.TryGetValue(normalizer.NormalizeLookup("المرحلة الفرعية"), out var subColumn) ||
                !headers.TryGetValue(normalizer.NormalizeLookup("سعر القطعة"), out var priceColumn))
            {
                issues.Add(Block("StageWorkbookSchemaInvalid", "The stages workbook must contain main stage, sub-stage, and final piece-price columns."));
                return result;
            }

            headers.TryGetValue(normalizer.NormalizeLookup("كود المرحلة"), out var codeColumn);
            headers.TryGetValue(normalizer.NormalizeLookup("ثواني القطعة"), out var secondsColumn);
            for (var row = range.RangeAddress.FirstAddress.RowNumber + 1; row <= range.RangeAddress.LastAddress.RowNumber; row++)
            {
                var main = Text(worksheet.Cell(row, mainColumn));
                var sub = Text(worksheet.Cell(row, subColumn));
                if (string.IsNullOrWhiteSpace(main) && string.IsNullOrWhiteSpace(sub)) continue;
                if (string.IsNullOrWhiteSpace(main) || string.IsNullOrWhiteSpace(sub))
                {
                    issues.Add(Block("InvalidStageIdentity", "Every stage row must contain both main-stage and sub-stage text."));
                    continue;
                }
                if (!TryDecimal(Text(worksheet.Cell(row, priceColumn)), out var price) || price < 0m)
                {
                    issues.Add(Block("InvalidPiecePrice", "Every stage row must contain a non-negative final piece price."));
                    continue;
                }
                decimal? seconds = null;
                if (secondsColumn > 0 && !string.IsNullOrWhiteSpace(Text(worksheet.Cell(row, secondsColumn))))
                {
                    if (!TryDecimal(Text(worksheet.Cell(row, secondsColumn)), out var parsedSeconds) || parsedSeconds <= 0m)
                    {
                        issues.Add(Block("InvalidStandardSeconds", "Standard seconds must be positive when supplied."));
                        continue;
                    }
                    seconds = parsedSeconds;
                }
                result.Add(new SourceStage(row, main, sub, codeColumn > 0 ? EmptyToNull(Text(worksheet.Cell(row, codeColumn))) : null, price, seconds));
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or IOException)
        {
            issues.Add(Block("StageWorkbookUnreadable", "The stages workbook could not be read."));
        }
        return result;
    }

    private IReadOnlyCollection<SourceWorker> ParseSalaryRows(byte[] content, List<PilotBootstrapIssueDto> issues)
    {
        var result = new List<SourceWorker>();
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheets.FirstOrDefault();
            if (worksheet is null || worksheet.RangeUsed() is not { } range)
            {
                issues.Add(Block("SalaryWorkbookSchemaInvalid", "The salary workbook has no used worksheet range."));
                return result;
            }

            // This controlled source has fixed columns: employee code, source name,
            // local department and salary. The source name is intentionally not an identity key.
            for (var row = range.RangeAddress.FirstAddress.RowNumber + 1; row <= range.RangeAddress.LastAddress.RowNumber; row++)
            {
                var code = EmptyToNull(Text(worksheet.Cell(row, 1)));
                if (code is null) continue;
                var department = EmptyToNull(Text(worksheet.Cell(row, 3)));
                if (!TryDecimal(Text(worksheet.Cell(row, 4)), out var rawSalary) || rawSalary < 0m)
                {
                    issues.Add(Block("InvalidSalary", "Salary must be a non-negative number."));
                    continue;
                }
                result.Add(new SourceWorker(row, code, department, rawSalary == 0m ? null : rawSalary));
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or IOException)
        {
            issues.Add(Block("SalaryWorkbookUnreadable", "The salary and department workbook could not be read."));
        }
        return result;
    }

    private Dictionary<string, int> HeaderMap(IXLWorksheet worksheet, int row, int firstColumn, int lastColumn) =>
        Enumerable.Range(firstColumn, lastColumn - firstColumn + 1)
            .Select(column => new { Column = column, Header = Text(worksheet.Cell(row, column)) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Header))
            .GroupBy(x => normalizer.NormalizeLookup(x.Header))
            .ToDictionary(x => x.Key, x => x.First().Column);

    private string StageIdentity(string main, string sub) => $"{normalizer.NormalizeLookup(main)}\u001F{normalizer.NormalizeLookup(sub)}";
    private static PilotBootstrapIssueDto Block(string code, string message) => new(Blocking, code, message);
    private static PilotBootstrapIssueDto Warn(string code, string message) => new(Warning, code, message);
    private static bool HasBlockingIssue(IEnumerable<PilotBootstrapIssueDto> issues) => issues.Any(x => x.Severity == Blocking);
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Text(IXLCell cell) => cell.GetFormattedString().Trim();
    private static bool TryDecimal(string value, out decimal result)
    {
        value = ToLatinDigits(value).Replace("٫", ".", StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result) ||
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("ar-EG"), out result);
    }
    private static string ToLatinDigits(string value) => value.Replace('٠', '0').Replace('١', '1').Replace('٢', '2').Replace('٣', '3').Replace('٤', '4').Replace('٥', '5').Replace('٦', '6').Replace('٧', '7').Replace('٨', '8').Replace('٩', '9');

    private sealed record SourceStage(int SourceRow, string MainStageName, string SubStageName, string? SourceCode, decimal PiecePrice, decimal? StandardSeconds);
    private sealed record SourceWorker(int SourceRow, string EmployeeCode, string? DepartmentName, decimal? Salary);
    private sealed record StagePlan(SourceStage Source, SubStage? Existing, ProductModelStage? Mapping, string Code, bool Generated, bool UsesProvisionalCompensation, List<PilotBootstrapIssueDto> Issues);
    private sealed record WorkerPlan(SourceWorker Source, Worker? Worker, WorkerSalaryHistory? CurrentSalary, List<PilotBootstrapIssueDto> Issues);
    private sealed record PreparedBootstrap(
        PilotMasterDataBootstrapInput Input,
        IReadOnlyCollection<SourceStage> SourceStages,
        IReadOnlyCollection<SourceWorker> SourceWorkers,
        Factory? Factory,
        ProductionLine? ProductionLine,
        ProductModel? Product,
        IReadOnlyCollection<StagePlan> Stages,
        IReadOnlyCollection<WorkerPlan> Workers,
        int UnexpectedProductStageMappings,
        IReadOnlyCollection<string> ExistingProductCompensationModes,
        List<PilotBootstrapIssueDto> Issues)
    {
        public IReadOnlyCollection<PilotBootstrapIssueDto> AllIssues => Issues.Concat(Stages.SelectMany(x => x.Issues)).Concat(Workers.SelectMany(x => x.Issues))
            .GroupBy(x => new { x.Severity, x.Code, x.Message })
            .Select(group => group.Count() == 1
                ? group.First()
                : new PilotBootstrapIssueDto(group.Key.Severity, group.Key.Code, $"{group.Count()} occurrences: {group.Key.Message}"))
            .ToArray();
    }
}
