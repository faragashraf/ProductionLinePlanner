using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Realtime;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Attendance;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

public sealed class OperationalReadinessEngine(
    AppDbContext dbContext,
    IOptions<AttendanceSourceOptions> sourceOptions,
    ICairoTimeZoneProvider cairoTimeZoneProvider,
    IAttendanceEngine? attendanceEngine = null,
    IAttendanceWorkdayPolicy? attendanceWorkdayPolicy = null,
    IAttendanceFreshnessEngine? attendanceFreshnessEngine = null) : IOperationalReadinessEngine
{
    private readonly AttendanceSourceOptions options = sourceOptions.Value;
    private readonly IAttendanceWorkdayPolicy workdayPolicy = attendanceWorkdayPolicy ??
        new AttendanceWorkdayPolicy(sourceOptions, cairoTimeZoneProvider);
    private readonly IAttendanceEngine attendanceEngine = attendanceEngine ??
        new AttendanceEngine(
            null!,
            null!,
            dbContext,
            cairoTimeZoneProvider,
            attendanceWorkdayPolicy ?? new AttendanceWorkdayPolicy(sourceOptions, cairoTimeZoneProvider));
    private readonly IAttendanceFreshnessEngine freshnessEngine = attendanceFreshnessEngine ??
        new AttendanceFreshnessEngine(
            dbContext,
            sourceOptions,
            attendanceWorkdayPolicy ?? new AttendanceWorkdayPolicy(sourceOptions, cairoTimeZoneProvider));

    public async Task<Result<OperationalReadinessSnapshotDto>> GetSnapshotAsync(
        Guid? factoryId = null,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        var asOf = NormalizeUtc(asOfUtc);
        var operationalDate = workdayPolicy.GetOperationalDate(asOf);
        var structure = await LoadStructureAsync(factoryId, asOf, cancellationToken);
        if (factoryId.HasValue && structure.Factories.All(factory => factory.Id != factoryId.Value))
            return Result<OperationalReadinessSnapshotDto>.Failure(new Error("NotFound", "Factory not found."));

        var freshness = await freshnessEngine.GetAsync(operationalDate, asOf, cancellationToken);
        var evidence = await LoadAttendanceEvidenceAsync(
            structure.Assignments.Select(item => item.WorkerId), operationalDate, freshness.IsTrusted, cancellationToken);
        var states = evidence.ToDictionary(
            pair => pair.Key,
            pair => new OperationalWorkerState(pair.Key, pair.Value.State, pair.Value.IsLate, pair.Value.HasCheckedOut));
        var modelNamesByLine = structure.StageCatalog
            .GroupBy(item => item.ProductionLineId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(item => item.ModelName).Distinct().Order().ToArray());
        var modelsByLine = structure.StageCatalog
            .GroupBy(item => item.ProductionLineId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<OperationalReadinessModelOptionDto>)group
                    .GroupBy(item => new { item.ModelId, item.ModelName, item.ModelCode })
                    .Select(model => new OperationalReadinessModelOptionDto(
                        model.Key.ModelId,
                        model.Key.ModelName,
                        model.Key.ModelCode,
                        model.Select(item => item.SubStageId).Distinct().Count()))
                    .OrderBy(model => model.Name)
                    .ToArray());

        var factories = structure.Factories.Select(factory =>
        {
            var departments = structure.Departments.Where(department => department.FactoryId == factory.Id).Select(department =>
            {
                var lines = structure.Lines.Where(line => line.DepartmentId == department.Id).Select(line =>
                {
                    var workerIds = structure.Assignments.Where(item => item.ProductionLineId == line.Id).Select(item => item.WorkerId);
                    var models = modelsByLine.GetValueOrDefault(line.Id) ?? [];
                    var childCount = models.Count > 0
                        ? models.Count
                        : structure.Assignments.Where(item => item.ProductionLineId == line.Id).Select(item => item.SubStageId).Distinct().Count();
                    return new OperationalReadinessLineDto(
                        line.Id,
                        factory.Id,
                        department.Id,
                        line.Name,
                        line.Code,
                        OperationalReadinessCalculator.Calculate(workerIds, states, freshness.IsTrusted, childCount),
                        modelNamesByLine.GetValueOrDefault(line.Id) ?? [],
                        models);
                }).OrderByReadiness(item => item.Metrics, item => item.Name).ToArray();

                var departmentWorkerIds = structure.Assignments
                    .Where(item => item.DepartmentId == department.Id)
                    .Select(item => item.WorkerId);
                return new OperationalReadinessDepartmentDto(
                    department.Id,
                    factory.Id,
                    department.Name,
                    department.Code,
                    OperationalReadinessCalculator.Calculate(departmentWorkerIds, states, freshness.IsTrusted, lines.Length),
                    lines);
            }).OrderByReadiness(item => item.Metrics, item => item.Name).ToArray();

            var factoryWorkerIds = structure.Assignments.Where(item => item.FactoryId == factory.Id).Select(item => item.WorkerId);
            return new OperationalReadinessFactoryDto(
                factory.Id,
                factory.Name,
                factory.Code,
                OperationalReadinessCalculator.Calculate(factoryWorkerIds, states, freshness.IsTrusted, departments.Length),
                departments);
        }).OrderByReadiness(item => item.Metrics, item => item.Name).ToArray();

        return Result<OperationalReadinessSnapshotDto>.Success(new OperationalReadinessSnapshotDto(
            operationalDate,
            asOf,
            new OperationalReadinessWorkdayPolicyDto(
                options.WorkdayBoundaryTime.ToString(@"hh\:mm"),
                options.DayStartTime.ToString(@"hh\:mm"),
                options.LateThresholdMinutes,
                options.FreshnessThresholdMinutes),
            freshness,
            factories));
    }

    public async Task<Result<OperationalReadinessStagesDto>> GetLineStagesAsync(
        Guid productionLineId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default,
        Guid? productModelId = null)
    {
        if (productionLineId == Guid.Empty)
            return Result<OperationalReadinessStagesDto>.Failure(new Error("ValidationError", "ProductionLineId is required."));

        var asOf = NormalizeUtc(asOfUtc);
        var line = await (from productionLine in dbContext.ProductionLines.AsNoTracking()
                          join department in dbContext.Departments.AsNoTracking() on productionLine.DepartmentId equals department.Id
                          join factory in dbContext.Factories.AsNoTracking() on productionLine.FactoryId equals factory.Id
                          where productionLine.Id == productionLineId && productionLine.IsActive && department.IsActive && factory.IsActive
                          select new LineContext(
                              factory.Id, factory.Name, department.Id, department.NameAr,
                              productionLine.Id, productionLine.Name)).SingleOrDefaultAsync(cancellationToken);
        if (line is null)
            return Result<OperationalReadinessStagesDto>.Failure(new Error("NotFound", "Production line not found."));

        var structure = await LoadStructureAsync(line.FactoryId, asOf, cancellationToken);
        var operationalDate = workdayPolicy.GetOperationalDate(asOf);
        var freshness = await freshnessEngine.GetAsync(operationalDate, asOf, cancellationToken);
        var assignments = structure.Assignments.Where(item => item.ProductionLineId == productionLineId).ToArray();
        var evidence = await LoadAttendanceEvidenceAsync(
            assignments.Select(item => item.WorkerId), operationalDate, freshness.IsTrusted, cancellationToken);
        var states = evidence.ToDictionary(
            pair => pair.Key,
            pair => new OperationalWorkerState(pair.Key, pair.Value.State, pair.Value.IsLate, pair.Value.HasCheckedOut));
        var lineCatalog = structure.StageCatalog.Where(item => item.ProductionLineId == productionLineId).ToArray();
        var availableModels = lineCatalog
            .GroupBy(item => new { item.ModelId, item.ModelName, item.ModelCode })
            .Select(model => new OperationalReadinessModelOptionDto(
                model.Key.ModelId,
                model.Key.ModelName,
                model.Key.ModelCode,
                model.Select(item => item.SubStageId).Distinct().Count()))
            .OrderBy(model => model.Name)
            .ToArray();
        if (productModelId.HasValue && availableModels.All(model => model.Id != productModelId.Value))
        {
            return Result<OperationalReadinessStagesDto>.Failure(
                new Error("ValidationError", "The selected model is not assigned to this production line."));
        }

        var effectiveModelId = productModelId ?? (availableModels.Length == 1 ? availableModels[0].Id : null);
        var requiresModelSelection = availableModels.Length > 1 && !effectiveModelId.HasValue;
        var selectedCatalog = effectiveModelId.HasValue
            ? lineCatalog.Where(item => item.ModelId == effectiveModelId.Value).ToArray()
            : availableModels.Length == 0 ? [] : lineCatalog;
        var catalogByStage = selectedCatalog
            .GroupBy(item => item.SubStageId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var stageIds = requiresModelSelection
            ? []
            : effectiveModelId.HasValue
                ? catalogByStage.Keys.ToArray()
                : assignments.Select(item => item.SubStageId).Distinct().ToArray();
        var stageDetails = await (from stage in dbContext.SubStages.AsNoTracking()
                                  join mainStage in dbContext.MainStages.AsNoTracking() on stage.MainStageId equals mainStage.Id
                                  where stageIds.Contains(stage.Id) && stage.IsActive && mainStage.IsActive
                                        && stage.DepartmentId == line.DepartmentId
                                  select new StageContext(
                                      stage.Id, stage.Name, stage.Code, stage.MainStageId, mainStage.Name))
            .ToArrayAsync(cancellationToken);

        var stages = stageDetails.Select(stage => new OperationalReadinessStageDto(
            stage.Id,
            line.FactoryId,
            line.DepartmentId,
            line.ProductionLineId,
            stage.MainStageId,
            stage.Name,
            stage.Code,
            stage.MainStageName,
            catalogByStage.GetValueOrDefault(stage.Id)?.Min(item => item.StageOrder),
            OperationalReadinessCalculator.Calculate(
                assignments.Where(item => item.SubStageId == stage.Id).Select(item => item.WorkerId),
                states,
                freshness.IsTrusted,
                assignments.Where(item => item.SubStageId == stage.Id).Select(item => item.WorkerId).Distinct().Count()),
            catalogByStage.GetValueOrDefault(stage.Id)?.Select(item => item.ModelName).Distinct().Order().ToArray() ?? []))
            .OrderBy(item => item.StageOrder is > 0 ? 0 : 1)
            .ThenBy(item => item.StageOrder is > 0 ? item.StageOrder : null)
            .ThenBy(item => item.Name)
            .ThenBy(item => item.Id)
            .ToArray();

        return Result<OperationalReadinessStagesDto>.Success(new OperationalReadinessStagesDto(
            operationalDate, asOf, freshness,
            line.FactoryId, line.FactoryName,
            line.DepartmentId, line.DepartmentName,
            line.ProductionLineId, line.ProductionLineName,
            effectiveModelId,
            availableModels.FirstOrDefault(model => model.Id == effectiveModelId)?.Name,
            requiresModelSelection,
            availableModels,
            stages));
    }

    public async Task<Result<OperationalReadinessWorkersDto>> GetStageWorkersAsync(
        Guid productionLineId,
        Guid stageId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (productionLineId == Guid.Empty || stageId == Guid.Empty)
            return Result<OperationalReadinessWorkersDto>.Failure(new Error("ValidationError", "ProductionLineId and StageId are required."));

        var asOf = NormalizeUtc(asOfUtc);
        var context = await (from productionLine in dbContext.ProductionLines.AsNoTracking()
                             join department in dbContext.Departments.AsNoTracking() on productionLine.DepartmentId equals department.Id
                             join factory in dbContext.Factories.AsNoTracking() on productionLine.FactoryId equals factory.Id
                             join stage in dbContext.SubStages.AsNoTracking() on department.Id equals stage.DepartmentId
                             where productionLine.Id == productionLineId && stage.Id == stageId
                                   && productionLine.IsActive && department.IsActive && factory.IsActive && stage.IsActive
                             select new WorkerListContext(
                                 factory.Id, factory.Name, department.Id, department.NameAr,
                                 productionLine.Id, productionLine.Name, stage.Id, stage.Name)).SingleOrDefaultAsync(cancellationToken);
        if (context is null)
            return Result<OperationalReadinessWorkersDto>.Failure(new Error("NotFound", "Production line stage not found."));

        var assignedWorkers = await (from assignment in dbContext.WorkerDefaultAssignments.AsNoTracking()
                                     join worker in dbContext.Workers.AsNoTracking() on assignment.WorkerId equals worker.Id
                                     where assignment.IsActive && assignment.AssignedAt <= asOf
                                           && assignment.ProductionLineId == productionLineId && assignment.SubStageId == stageId
                                           && worker.IsActive && worker.EmploymentStatus == EmploymentStatus.Active
                                     select new WorkerContext(worker.Id, worker.EmployeeCode, worker.FullName))
            .Distinct().ToArrayAsync(cancellationToken);
        var operationalDate = workdayPolicy.GetOperationalDate(asOf);
        var freshness = await freshnessEngine.GetAsync(operationalDate, asOf, cancellationToken);
        var evidence = await LoadAttendanceEvidenceAsync(
            assignedWorkers.Select(worker => worker.Id), operationalDate, freshness.IsTrusted, cancellationToken);

        var workers = assignedWorkers.Select(worker =>
        {
            var item = evidence.GetValueOrDefault(worker.Id) ?? AttendanceEvidence.Unknown;
            return new OperationalReadinessWorkerDto(
                worker.Id,
                productionLineId,
                stageId,
                worker.EmployeeCode,
                worker.FullName,
                item.State,
                AttendanceLabel(item.State),
                item.State is OperationalAttendanceStates.Present or OperationalAttendanceStates.Late,
                item.CheckInAtUtc,
                item.CheckOutAtUtc,
                item.LateByMinutes);
        }).OrderBy(worker => AttendancePriority(worker.AttendanceState))
          .ThenBy(worker => worker.FullName)
          .ToArray();

        return Result<OperationalReadinessWorkersDto>.Success(new OperationalReadinessWorkersDto(
            operationalDate, asOf, freshness,
            context.FactoryId, context.FactoryName,
            context.DepartmentId, context.DepartmentName,
            context.ProductionLineId, context.ProductionLineName,
            context.StageId, context.StageName,
            workers));
    }

    public async Task<Result<OperationalReadinessDeltaDto>> GetDeltaAsync(
        ManufacturingDataChanged change,
        CancellationToken cancellationToken = default)
    {
        var asOf = DateTime.UtcNow;
        var operationalDate = workdayPolicy.GetOperationalDate(asOf);
        var freshness = await freshnessEngine.GetAsync(operationalDate, asOf, cancellationToken);
        if (change.EntityType == ManufacturingEntityType.AttendanceSyncState)
        {
            return Result<OperationalReadinessDeltaDto>.Success(new OperationalReadinessDeltaDto(
                change.EventId, operationalDate, asOf, freshness, false, [], []));
        }

        if (change.EntityType is ManufacturingEntityType.Factory
            or ManufacturingEntityType.Department
            or ManufacturingEntityType.ProductionLine
            or ManufacturingEntityType.MainStage
            or ManufacturingEntityType.SubStage
            or ManufacturingEntityType.ProductModel
            or ManufacturingEntityType.ProductModelStage)
        {
            return Result<OperationalReadinessDeltaDto>.Success(new OperationalReadinessDeltaDto(
                change.EventId, operationalDate, asOf, freshness, true, [], []));
        }

        var workerIds = (change.WorkerIds ?? [])
            .Concat(change.WorkerId.HasValue ? [change.WorkerId.Value] : [])
            .Where(id => id != Guid.Empty).Distinct().ToArray();
        var locations = await LoadAffectedLocationsAsync(change, workerIds, cancellationToken);
        if (locations.Length == 0 && !change.FactoryId.HasValue)
        {
            return Result<OperationalReadinessDeltaDto>.Success(new OperationalReadinessDeltaDto(
                change.EventId, operationalDate, asOf, freshness, true, [], []));
        }

        var factoryIds = locations.Select(item => item.FactoryId)
            .Concat(change.FactoryId.HasValue ? [change.FactoryId.Value] : [])
            .Distinct().ToArray();
        var snapshots = new List<OperationalReadinessSnapshotDto>();
        foreach (var factoryId in factoryIds)
        {
            var snapshot = await GetSnapshotAsync(factoryId, asOf, cancellationToken);
            if (snapshot.IsFailure) return Result<OperationalReadinessDeltaDto>.Failure(snapshot.Error!);
            snapshots.Add(snapshot.Value!);
        }

        var departmentIds = locations.Select(item => item.DepartmentId)
            .Concat(change.DepartmentId.HasValue ? [change.DepartmentId.Value] : []).Distinct().ToHashSet();
        var lineIds = locations.Select(item => item.ProductionLineId)
            .Concat(change.ProductionLineId.HasValue ? [change.ProductionLineId.Value] : []).Distinct().ToHashSet();
        var stageIds = locations.Select(item => item.SubStageId)
            .Concat(change.SubStageId.HasValue ? [change.SubStageId.Value] : []).Distinct().ToHashSet();
        var nodePatches = new List<OperationalReadinessNodePatchDto>();
        foreach (var factory in snapshots.SelectMany(snapshot => snapshot.Factories))
        {
            nodePatches.Add(NodePatch(factory.Id, null, OperationalReadinessNodeTypes.Factory, factory.Name, factory.Code, factory.Metrics));
            foreach (var department in factory.Departments.Where(item => departmentIds.Contains(item.Id)))
            {
                nodePatches.Add(NodePatch(department.Id, factory.Id, OperationalReadinessNodeTypes.Department, department.Name, department.Code, department.Metrics));
                foreach (var line in department.ProductionLines.Where(item => lineIds.Contains(item.Id)))
                    nodePatches.Add(NodePatch(line.Id, department.Id, OperationalReadinessNodeTypes.ProductionLine, line.Name, line.Code, line.Metrics, line.ModelNames));
            }
        }

        var workerPatches = new List<OperationalReadinessWorkerPatchDto>();
        foreach (var lineId in lineIds)
        {
            var affectedModelId = await dbContext.ProductModelStages.AsNoTracking()
                .Where(item => item.ProductionLineId == lineId && item.IsActive && item.IsRequired
                               && stageIds.Contains(item.SubStageId))
                .OrderBy(item => item.ProductModelId)
                .Select(item => (Guid?)item.ProductModelId)
                .FirstOrDefaultAsync(cancellationToken);
            var stagesResult = await GetLineStagesAsync(lineId, asOf, cancellationToken, affectedModelId);
            if (stagesResult.IsFailure) continue;
            foreach (var stage in stagesResult.Value!.Stages.Where(item => stageIds.Contains(item.Id)))
            {
                nodePatches.Add(NodePatch(stage.Id, lineId, OperationalReadinessNodeTypes.Stage, stage.Name, stage.Code, stage.Metrics, stage.ModelNames));
                if (workerIds.Length == 0) continue;
                var workersResult = await GetStageWorkersAsync(lineId, stage.Id, asOf, cancellationToken);
                if (workersResult.IsFailure) continue;
                foreach (var workerId in workerIds)
                {
                    var worker = workersResult.Value!.Workers.FirstOrDefault(item => item.WorkerId == workerId);
                    workerPatches.Add(new OperationalReadinessWorkerPatchDto(lineId, stage.Id, workerId, worker is null, worker));
                }
            }
        }

        return Result<OperationalReadinessDeltaDto>.Success(new OperationalReadinessDeltaDto(
            change.EventId,
            operationalDate,
            asOf,
            freshness,
            false,
            nodePatches.DistinctBy(item => (item.NodeType, item.Id)).ToArray(),
            workerPatches.DistinctBy(item => (item.ProductionLineId, item.StageId, item.WorkerId)).ToArray()));
    }

    private async Task<StructureContext> LoadStructureAsync(Guid? factoryId, DateTime asOf, CancellationToken cancellationToken)
    {
        var factories = await dbContext.Factories.AsNoTracking()
            .Where(factory => factory.IsActive && (!factoryId.HasValue || factory.Id == factoryId.Value))
            .OrderBy(factory => factory.Name)
            .Select(factory => new FactoryContext(factory.Id, factory.Name, factory.Code))
            .ToArrayAsync(cancellationToken);
        var factoryIds = factories.Select(factory => factory.Id).ToArray();
        var departments = await dbContext.Departments.AsNoTracking()
            .Where(department => department.IsActive && factoryIds.Contains(department.FactoryId))
            .OrderBy(department => department.SequenceOrder)
            .Select(department => new DepartmentContext(department.Id, department.FactoryId, department.NameAr, department.Code))
            .ToArrayAsync(cancellationToken);
        var departmentIds = departments.Select(department => department.Id).ToArray();
        var lines = await dbContext.ProductionLines.AsNoTracking()
            .Where(line => line.IsActive && line.DepartmentId.HasValue && departmentIds.Contains(line.DepartmentId.Value))
            .OrderBy(line => line.SequenceOrder)
            .Select(line => new ProductionLineContext(line.Id, line.FactoryId, line.DepartmentId!.Value, line.Name, line.LineCode))
            .ToArrayAsync(cancellationToken);
        var lineIds = lines.Select(line => line.Id).ToArray();
        var assignments = await (from assignment in dbContext.WorkerDefaultAssignments.AsNoTracking()
                                 join worker in dbContext.Workers.AsNoTracking() on assignment.WorkerId equals worker.Id
                                 join stage in dbContext.SubStages.AsNoTracking() on assignment.SubStageId equals stage.Id
                                 join mainStage in dbContext.MainStages.AsNoTracking() on stage.MainStageId equals mainStage.Id
                                 join line in dbContext.ProductionLines.AsNoTracking() on assignment.ProductionLineId equals line.Id
                                 join department in dbContext.Departments.AsNoTracking() on line.DepartmentId equals department.Id
                                 join factory in dbContext.Factories.AsNoTracking() on line.FactoryId equals factory.Id
                                 where assignment.IsActive && assignment.AssignedAt <= asOf
                                       && worker.IsActive && worker.EmploymentStatus == EmploymentStatus.Active
                                       && stage.IsActive && mainStage.IsActive && line.IsActive && department.IsActive && factory.IsActive
                                       && lineIds.Contains(line.Id) && stage.DepartmentId == department.Id
                                 select new AssignmentContext(
                                     assignment.Id, worker.Id, worker.EmployeeCode, worker.FullName,
                                     factory.Id, department.Id, line.Id, stage.Id,
                                     stage.MainStageId, stage.Name, stage.Code, mainStage.Name))
            .Distinct().ToArrayAsync(cancellationToken);
        var stageCatalog = await (from modelStage in dbContext.ProductModelStages.AsNoTracking()
                                  join model in dbContext.ProductModels.AsNoTracking() on modelStage.ProductModelId equals model.Id
                                  join stage in dbContext.SubStages.AsNoTracking() on modelStage.SubStageId equals stage.Id
                                  join mainStage in dbContext.MainStages.AsNoTracking() on stage.MainStageId equals mainStage.Id
                                  where modelStage.IsActive && modelStage.IsRequired && model.IsActive && stage.IsActive && mainStage.IsActive
                                        && lineIds.Contains(modelStage.ProductionLineId)
                                  select new StageCatalogContext(
                                      modelStage.ProductionLineId, stage.Id, stage.MainStageId,
                                      model.Id, model.Name, model.Code, modelStage.StageOrder))
            .Distinct().ToArrayAsync(cancellationToken);

        return new StructureContext(factories, departments, lines, assignments, stageCatalog);
    }

    private async Task<Dictionary<Guid, AttendanceEvidence>> LoadAttendanceEvidenceAsync(
        IEnumerable<Guid> workerIds,
        DateOnly operationalDate,
        bool isTrusted,
        CancellationToken cancellationToken)
    {
        var ids = workerIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        if (!isTrusted) return ids.ToDictionary(id => id, _ => AttendanceEvidence.Unknown);

        var presenceResult = await attendanceEngine.GetPresenceWindowsByWorkerAsync(ids, operationalDate, cancellationToken);
        if (presenceResult.IsFailure)
            return ids.ToDictionary(id => id, _ => AttendanceEvidence.Unknown);
        var presence = presenceResult.Value!;
        var shiftStartLocal = workdayPolicy.GetShiftStartLocal(operationalDate);

        return ids.ToDictionary(workerId => workerId, workerId =>
        {
            var state = AttendanceCompletionClassifier.Resolve(presence.GetValueOrDefault(workerId), true);
            var lateMinutes = state.IsLate && state.FirstInUtc.HasValue
                ? Math.Max(0, (int)Math.Floor((TimeZoneInfo.ConvertTimeFromUtc(state.FirstInUtc.Value, cairoTimeZoneProvider.TimeZone) - shiftStartLocal).TotalMinutes))
                : (int?)null;
            return new AttendanceEvidence(
                state.State,
                state.IsLate,
                state.HasCheckedOut,
                state.FirstInUtc,
                state.LastOutUtc,
                lateMinutes);
        });
    }

    private async Task<AffectedLocation[]> LoadAffectedLocationsAsync(
        ManufacturingDataChanged change,
        IReadOnlyCollection<Guid> workerIds,
        CancellationToken cancellationToken)
    {
        var query = from assignment in dbContext.WorkerDefaultAssignments.AsNoTracking()
                    join stage in dbContext.SubStages.AsNoTracking() on assignment.SubStageId equals stage.Id
                    join line in dbContext.ProductionLines.AsNoTracking() on assignment.ProductionLineId equals line.Id
                    where (workerIds.Count > 0 && workerIds.Contains(assignment.WorkerId))
                          || (change.EntityType == ManufacturingEntityType.WorkerDefaultAssignment && assignment.Id == change.EntityId)
                    select new AffectedLocation(
                        assignment.WorkerId,
                        line.FactoryId,
                        line.DepartmentId ?? stage.DepartmentId,
                        line.Id,
                        stage.Id);
        var locations = await query.Distinct().ToArrayAsync(cancellationToken);
        if (locations.Length > 0) return locations;
        if (change.ProductionLineId.HasValue && change.SubStageId.HasValue)
        {
            var fallback = await (from line in dbContext.ProductionLines.AsNoTracking()
                                  join stage in dbContext.SubStages.AsNoTracking() on change.SubStageId.Value equals stage.Id
                                  where line.Id == change.ProductionLineId.Value
                                  select new AffectedLocation(
                                      change.WorkerId ?? Guid.Empty,
                                      line.FactoryId,
                                      line.DepartmentId ?? stage.DepartmentId,
                                      line.Id,
                                      stage.Id)).SingleOrDefaultAsync(cancellationToken);
            return fallback is null ? [] : [fallback];
        }
        return [];
    }

    private static OperationalReadinessNodePatchDto NodePatch(
        Guid id,
        Guid? parentId,
        string type,
        string name,
        string? code,
        OperationalReadinessMetricsDto metrics,
        IReadOnlyList<string>? modelNames = null) =>
        new(id, parentId, type, name, code, metrics, modelNames ?? []);

    private static DateTime NormalizeUtc(DateTime? value)
    {
        var result = value ?? DateTime.UtcNow;
        return result.Kind == DateTimeKind.Utc ? result : result.ToUniversalTime();
    }

    private static string AttendanceLabel(string state) => state switch
    {
        OperationalAttendanceStates.Present => "حاضر",
        OperationalAttendanceStates.Late => "متأخر",
        OperationalAttendanceStates.Absent => "غائب",
        OperationalAttendanceStates.NotCheckedIn => "لم يسجل حضورًا",
        OperationalAttendanceStates.CheckedOut => "سجل انصرافًا",
        _ => "حالة الحضور غير مؤكدة"
    };

    private static int AttendancePriority(string state) => state switch
    {
        OperationalAttendanceStates.Absent => 0,
        OperationalAttendanceStates.NotCheckedIn => 1,
        OperationalAttendanceStates.CheckedOut => 2,
        OperationalAttendanceStates.Unknown => 3,
        OperationalAttendanceStates.Late => 4,
        _ => 5
    };

    private sealed record FactoryContext(Guid Id, string Name, string Code);
    private sealed record DepartmentContext(Guid Id, Guid FactoryId, string Name, string Code);
    private sealed record ProductionLineContext(Guid Id, Guid FactoryId, Guid DepartmentId, string Name, string? Code);
    private sealed record AssignmentContext(
        Guid AssignmentId,
        Guid WorkerId,
        string EmployeeCode,
        string FullName,
        Guid FactoryId,
        Guid DepartmentId,
        Guid ProductionLineId,
        Guid SubStageId,
        Guid MainStageId,
        string StageName,
        string StageCode,
        string MainStageName);
    private sealed record StageCatalogContext(
        Guid ProductionLineId,
        Guid SubStageId,
        Guid MainStageId,
        Guid ModelId,
        string ModelName,
        string ModelCode,
        int StageOrder);
    private sealed record StructureContext(
        FactoryContext[] Factories,
        DepartmentContext[] Departments,
        ProductionLineContext[] Lines,
        AssignmentContext[] Assignments,
        StageCatalogContext[] StageCatalog);
    private sealed record LineContext(
        Guid FactoryId,
        string FactoryName,
        Guid DepartmentId,
        string DepartmentName,
        Guid ProductionLineId,
        string ProductionLineName);
    private sealed record StageContext(Guid Id, string Name, string Code, Guid MainStageId, string MainStageName);
    private sealed record WorkerListContext(
        Guid FactoryId,
        string FactoryName,
        Guid DepartmentId,
        string DepartmentName,
        Guid ProductionLineId,
        string ProductionLineName,
        Guid StageId,
        string StageName);
    private sealed record WorkerContext(Guid Id, string EmployeeCode, string FullName);
    private sealed record AffectedLocation(Guid WorkerId, Guid FactoryId, Guid DepartmentId, Guid ProductionLineId, Guid SubStageId);
    private sealed record AttendanceEvidence(
        string State,
        bool IsLate,
        bool HasCheckedOut,
        DateTime? CheckInAtUtc,
        DateTime? CheckOutAtUtc,
        int? LateByMinutes)
    {
        public static readonly AttendanceEvidence Unknown = new(OperationalAttendanceStates.Unknown, false, false, null, null, null);
    }
}

internal static class OperationalReadinessOrderingExtensions
{
    public static IOrderedEnumerable<T> OrderByReadiness<T>(
        this IEnumerable<T> source,
        Func<T, OperationalReadinessMetricsDto> metrics,
        Func<T, string> name) => source
        .OrderByDescending(item => metrics(item).ContributionToParentShortage ?? -1)
        .ThenBy(item => metrics(item).OperationalReadinessPercentage ?? decimal.MaxValue)
        .ThenBy(name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("ar"), false));
}
