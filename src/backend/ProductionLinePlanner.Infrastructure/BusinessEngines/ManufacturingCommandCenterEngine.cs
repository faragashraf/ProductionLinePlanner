using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

public sealed class ManufacturingCommandCenterEngine(
    AppDbContext db,
    IAttendanceEngine attendanceEngine) : IManufacturingCommandCenterEngine
{
    private const string AllStatuses = "All";
    private static readonly string[] SupportedOperationStatuses =
        [AllStatuses, "None", "Draft", "Approved", "ApprovalCancelled", "Cancelled"];

    public async Task<Result<ManufacturingCommandCenterDto>> GetAsync(
        ManufacturingCommandCenterQuery query,
        CancellationToken cancellationToken = default)
    {
        var operationStatus = NormalizeOperationStatus(query.OperationStatus);
        if (operationStatus is null)
        {
            return Result<ManufacturingCommandCenterDto>.Failure(new Error(
                "ValidationError",
                "OperationStatus must be All, None, Draft, Approved, ApprovalCancelled, or Cancelled."));
        }

        var calculatedAtUtc = DateTime.UtcNow;
        var factories = await db.Factories.AsNoTracking()
            .Where(factory => factory.IsActive)
            .OrderBy(factory => factory.Name)
            .Select(factory => new FactoryRow(factory.Id, factory.Name, factory.Code))
            .ToArrayAsync(cancellationToken);
        var activeFactoryIds = factories.Select(factory => factory.Id).ToArray();
        var departments = await db.Departments.AsNoTracking()
            .Where(department => department.IsActive && activeFactoryIds.Contains(department.FactoryId))
            .OrderBy(department => department.SequenceOrder)
            .ThenBy(department => department.NameAr)
            .Select(department => new DepartmentRow(department.Id, department.FactoryId, department.NameAr, department.Code))
            .ToArrayAsync(cancellationToken);
        var activeDepartmentIds = departments.Select(department => department.Id).ToArray();
        var catalogLines = await db.ProductionLines.AsNoTracking()
            .Where(line => line.IsActive
                && activeFactoryIds.Contains(line.FactoryId)
                && (!line.DepartmentId.HasValue || activeDepartmentIds.Contains(line.DepartmentId.Value)))
            .OrderBy(line => line.SequenceOrder)
            .ThenBy(line => line.Name)
            .Select(line => new LineRow(
                line.Id,
                line.FactoryId,
                line.DepartmentId,
                line.Name,
                line.LineCode,
                line.UpdatedAtUtc))
            .ToArrayAsync(cancellationToken);

        var attributionComplete = (!query.FactoryId.HasValue && !query.DepartmentId.HasValue
                && !query.ProductionLineId.HasValue && operationStatus == AllStatuses)
            || (factories.Length == 1 && query.FactoryId == factories[0].Id
                && !query.DepartmentId.HasValue && !query.ProductionLineId.HasValue
                && operationStatus == AllStatuses);
        var scopedLines = catalogLines
            .Where(line => !query.FactoryId.HasValue || line.FactoryId == query.FactoryId.Value)
            .Where(line => !query.DepartmentId.HasValue || line.DepartmentId == query.DepartmentId.Value)
            .Where(line => !query.ProductionLineId.HasValue || line.Id == query.ProductionLineId.Value)
            .ToArray();
        var scopedLineIds = scopedLines.Select(line => line.Id).ToArray();

        var operationRows = scopedLineIds.Length == 0
            ? []
            : await db.ProductionOrders.AsNoTracking()
                .Where(order => order.ProductionDate == query.ProductionDate
                    && order.ProductionLineId.HasValue
                    && scopedLineIds.Contains(order.ProductionLineId.Value)
                    && (order.SourceReference != null || order.SourceImportBatchId != null))
                .Select(order => new OperationRow(
                    order.Id,
                    order.ProductionLineId!.Value,
                    order.ProductModelId,
                    order.ProductModel!.Code,
                    order.ProductModel.Name,
                    order.Status,
                    order.PlannedQuantity,
                    order.RecordedAtUtc,
                    order.UpdatedAtUtc,
                    order.ApprovedAtUtc))
                .ToArrayAsync(cancellationToken);
        var operationIds = operationRows.Select(operation => operation.Id).ToArray();
        var productionRecords = operationIds.Length == 0
            ? []
            : await db.StageProductionRecords.AsNoTracking()
                .Where(record => operationIds.Contains(record.ProductionOrderId))
                .Select(record => new ProductionRecordRow(
                    record.Id,
                    record.ProductionOrderId,
                    record.ProductModelStageId,
                    record.Status,
                    record.TotalWorkerEarnings,
                    record.CreatedAtUtc,
                    record.ApprovedAtUtc,
                    record.CancelledAtUtc,
                    record.ApprovalCancellationReason))
                .ToArrayAsync(cancellationToken);

        var recordsByOrder = productionRecords
            .GroupBy(record => record.ProductionOrderId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var operationStateById = operationRows.ToDictionary(
            operation => operation.Id,
            operation => OperationState(operation, recordsByOrder.GetValueOrDefault(operation.Id, [])));
        if (operationStatus != AllStatuses)
        {
            scopedLines = scopedLines
                .Where(line => LineMatchesOperationStatus(
                    line.Id,
                    operationStatus,
                    operationRows,
                    operationStateById))
                .ToArray();
            scopedLineIds = scopedLines.Select(line => line.Id).ToArray();
            operationRows = operationStatus == "None"
                ? []
                : operationRows.Where(operation => scopedLineIds.Contains(operation.ProductionLineId)
                    && operationStateById[operation.Id] == operationStatus).ToArray();
            operationIds = operationRows.Select(operation => operation.Id).ToArray();
            productionRecords = productionRecords.Where(record => operationIds.Contains(record.ProductionOrderId)).ToArray();
            recordsByOrder = productionRecords.GroupBy(record => record.ProductionOrderId)
                .ToDictionary(group => group.Key, group => group.ToArray());
        }

        var assignments = scopedLineIds.Length == 0
            ? []
            : await (from assignment in db.WorkerDefaultAssignments.AsNoTracking()
                     join worker in db.Workers.AsNoTracking() on assignment.WorkerId equals worker.Id
                     join subStage in db.SubStages.AsNoTracking() on assignment.SubStageId equals subStage.Id
                     join mainStage in db.MainStages.AsNoTracking() on subStage.MainStageId equals mainStage.Id
                     where assignment.IsActive
                         && worker.IsActive && worker.EmploymentStatus == EmploymentStatus.Active
                         && subStage.IsActive
                         && mainStage.IsActive
                         && scopedLineIds.Contains(mainStage.ProductionLineId)
                     select new AssignmentRow(
                         assignment.WorkerId,
                         worker.EmployeeCode,
                         worker.FullName,
                         assignment.SubStageId,
                         mainStage.ProductionLineId,
                         assignment.UpdatedAtUtc,
                         subStage.Name))
                .ToArrayAsync(cancellationToken);

        var attendanceWorkers = attributionComplete
            ? await db.Workers.AsNoTracking()
                .Where(worker => worker.IsActive && worker.EmploymentStatus == EmploymentStatus.Active)
                .Select(worker => new WorkerRow(worker.Id, worker.EmployeeCode, worker.FullName))
                .ToArrayAsync(cancellationToken)
            : assignments
                .GroupBy(assignment => assignment.WorkerId)
                .Select(group => new WorkerRow(group.Key, group.First().WorkerCode, group.First().WorkerName))
                .ToArray();
        // Noon UTC always resolves to the same Cairo calendar date. The attendance engine remains
        // responsible for the project's authoritative Cairo workday boundary and status mapping.
        var attendanceAsOfUtc = DateTime.SpecifyKind(
            query.ProductionDate.ToDateTime(new TimeOnly(12, 0)),
            DateTimeKind.Utc);
        var attendanceResult = await attendanceEngine.GetLatestAttendanceStatusByWorkerAsync(
            attendanceWorkers.Select(worker => worker.Id),
            attendanceAsOfUtc,
            cancellationToken);
        if (attendanceResult.IsFailure)
        {
            return Result<ManufacturingCommandCenterDto>.Failure(attendanceResult.Error!);
        }

        var attendance = attendanceResult.Value!;
        var presentWorkerIds = attendance
            .Where(item => item.Value.Status is AttendanceStatus.Present or AttendanceStatus.Late)
            .Select(item => item.Key)
            .ToHashSet();
        var assignedWorkerIds = assignments.Select(assignment => assignment.WorkerId).ToHashSet();
        var assignedPresentIds = assignedWorkerIds.Where(presentWorkerIds.Contains).ToHashSet();
        var assignedNotPresentIds = assignedWorkerIds.Where(id => !presentWorkerIds.Contains(id)).ToHashSet();

        var allActiveLineIds = attributionComplete ? catalogLines.Select(line => line.Id).ToArray() : [];
        var allActiveAssignedWorkerIds = allActiveLineIds.Length == 0
            ? []
            : await (from assignment in db.WorkerDefaultAssignments.AsNoTracking()
                     join worker in db.Workers.AsNoTracking() on assignment.WorkerId equals worker.Id
                     join subStage in db.SubStages.AsNoTracking() on assignment.SubStageId equals subStage.Id
                     join mainStage in db.MainStages.AsNoTracking() on subStage.MainStageId equals mainStage.Id
                     where assignment.IsActive && worker.IsActive && worker.EmploymentStatus == EmploymentStatus.Active
                         && subStage.IsActive && mainStage.IsActive
                         && allActiveLineIds.Contains(mainStage.ProductionLineId)
                     select assignment.WorkerId)
                .Distinct()
                .ToArrayAsync(cancellationToken);
        var presentWorkers = attributionComplete ? presentWorkerIds.Count : assignedPresentIds.Count;
        var presentUnassignedIds = attributionComplete
            ? presentWorkerIds.Where(id => !allActiveAssignedWorkerIds.Contains(id)).ToHashSet()
            : [];
        var coverage = Ratio(
            assignedPresentIds.Count,
            attributionComplete ? presentWorkerIds.Count : 0,
            ScopeDescription(query, operationStatus),
            query.ProductionDate,
            attributionComplete);

        var modelIds = operationRows.Select(operation => operation.ProductModelId).Distinct().ToArray();
        var journeyRows = modelIds.Length == 0 || scopedLineIds.Length == 0
            ? []
            : await (from modelStage in db.ProductModelStages.AsNoTracking()
                     join subStage in db.SubStages.AsNoTracking() on modelStage.SubStageId equals subStage.Id
                     join mainStage in db.MainStages.AsNoTracking() on subStage.MainStageId equals mainStage.Id
                     where modelStage.IsActive && modelStage.IsRequired
                         && subStage.IsActive && mainStage.IsActive
                         && modelIds.Contains(modelStage.ProductModelId)
                         && scopedLineIds.Contains(mainStage.ProductionLineId)
                     select new JourneyStageRow(
                         modelStage.Id,
                         modelStage.ProductModelId,
                         modelStage.SubStageId,
                         mainStage.ProductionLineId,
                         mainStage.Name,
                         subStage.Code,
                         subStage.Name,
                         modelStage.StageOrder,
                         modelStage.PiecePrice,
                         modelStage.StandardSeconds,
                         subStage.Capacity,
                         modelStage.UpdatedAtUtc))
                .ToArrayAsync(cancellationToken);

        var configurationJourneyRows = scopedLineIds.Length == 0
            ? []
            : await (from modelStage in db.ProductModelStages.AsNoTracking()
                     join model in db.ProductModels.AsNoTracking() on modelStage.ProductModelId equals model.Id
                     join subStage in db.SubStages.AsNoTracking() on modelStage.SubStageId equals subStage.Id
                     join mainStage in db.MainStages.AsNoTracking() on subStage.MainStageId equals mainStage.Id
                     where modelStage.IsActive && modelStage.IsRequired && model.IsActive
                         && subStage.IsActive && mainStage.IsActive
                         && scopedLineIds.Contains(mainStage.ProductionLineId)
                     select new JourneyStageRow(
                         modelStage.Id,
                         modelStage.ProductModelId,
                         modelStage.SubStageId,
                         mainStage.ProductionLineId,
                         mainStage.Name,
                         subStage.Code,
                         subStage.Name,
                         modelStage.StageOrder,
                         modelStage.PiecePrice,
                         modelStage.StandardSeconds,
                         subStage.Capacity,
                         modelStage.UpdatedAtUtc))
                .ToArrayAsync(cancellationToken);
        var availableJourneyLineIds = configurationJourneyRows.Select(stage => stage.ProductionLineId).Distinct().ToArray();

        var commandOperations = operationRows
            .OrderBy(operation => operation.ProductionLineId)
            .ThenBy(operation => operation.ProductModelCode)
            .Select(operation => BuildOperation(
                operation,
                operationStateById[operation.Id],
                journeyRows.Where(stage => stage.ProductModelId == operation.ProductModelId
                    && stage.ProductionLineId == operation.ProductionLineId).ToArray(),
                recordsByOrder.GetValueOrDefault(operation.Id, []),
                assignments,
                presentWorkerIds,
                query.ProductionDate,
                ScopeDescription(query, operationStatus)))
            .ToArray();
        var operationsByLine = commandOperations.GroupBy(operation => operation.ProductionLineId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var assignmentsByLine = assignments.GroupBy(assignment => assignment.ProductionLineId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var commandLines = scopedLines.Select(line =>
        {
            var lineOperations = operationsByLine.GetValueOrDefault(line.Id, []);
            var lineAssignments = assignmentsByLine.GetValueOrDefault(line.Id, []);
            var stages = lineOperations.SelectMany(operation => operation.Stages).ToArray();
            var hasJourney = lineOperations.Length > 0
                ? stages.Length > 0
                : availableJourneyLineIds.Contains(line.Id);
            var alerts = new List<string>();
            if (lineOperations.Length == 0) alerts.Add("لا يوجد تشغيل مسجل لهذا التاريخ.");
            if (!hasJourney) alerts.Add("لا توجد رحلة موديل قابلة للتشغيل على الخط.");
            var incompleteStages = stages.Count(stage => !stage.HasPrice || !stage.HasStandardTime);
            if (incompleteStages > 0) alerts.Add($"{incompleteStages} مرحلة ببيانات سعر أو زمن غير مكتملة.");
            var stagesWithoutPresent = stages.Count(stage => stage.PresentPermanentlyAssignedWorkers == 0);
            if (stagesWithoutPresent > 0) alerts.Add($"{stagesWithoutPresent} مرحلة مطلوبة بلا عامل حاضر مسكن دائم.");
            var understaffedStages = stages.Count(stage => stage.PresentPermanentlyAssignedWorkers < stage.RequiredWorkers);
            if (understaffedStages > 0) alerts.Add($"{understaffedStages} مرحلة دون تغطية الحضور المطلوبة.");

            var readinessStatus = !hasJourney
                    ? "JourneyNotConfigured"
                : lineOperations.Length == 0
                    ? "NoOperation"
                    : incompleteStages > 0
                        ? "DataIncomplete"
                        : understaffedStages > 0
                            ? "StaffingShortage"
                            : "Ready";
            var lastUpdate = new[]
                {
                    line.UpdatedAtUtc,
                    lineOperations.Select(operation => operation.LastReliableUpdateUtc).DefaultIfEmpty(line.UpdatedAtUtc).Max(),
                    lineAssignments.Select(assignment => assignment.UpdatedAtUtc).DefaultIfEmpty(line.UpdatedAtUtc).Max()
                }
                .Max();
            return new CommandCenterLineDto(
                line.Id,
                line.FactoryId,
                line.DepartmentId,
                line.Name,
                line.Code,
                readinessStatus,
                lineAssignments.Select(assignment => assignment.WorkerId).Distinct().Count(),
                lineAssignments.Where(assignment => presentWorkerIds.Contains(assignment.WorkerId)).Select(assignment => assignment.WorkerId).Distinct().Count(),
                stages.Sum(stage => stage.RequiredWorkers),
                stages.Length,
                stages.Count(stage => stage.PresentPermanentlyAssignedWorkers > 0),
                stagesWithoutPresent,
                lastUpdate,
                alerts,
                lineOperations);
        }).ToArray();

        // Data-quality warnings describe the active configured journeys in scope, not only
        // the model selected by a persisted order. A configured line with no order must not
        // hide a missing price or standard time.
        var qualityIssues = BuildQualityIssues(commandLines, configurationJourneyRows, scopedLines);
        int? modelsWithoutJourney = null;
        var modelsWithoutJourneyScopeNote = "متاح فقط في نطاق كل المصانع لأن الموديل بلا رحلة لا يمكن نسبه إلى مصنع أو خط.";
        if (!query.FactoryId.HasValue && !query.DepartmentId.HasValue && !query.ProductionLineId.HasValue
            && operationStatus == AllStatuses)
        {
            var orphanModels = await db.ProductModels.AsNoTracking()
                .Where(model => model.IsActive
                    && !db.ProductModelStages.Any(stage => stage.ProductModelId == model.Id
                        && stage.IsActive && stage.IsRequired))
                .Select(model => new { model.Id, model.Code, model.Name })
                .ToArrayAsync(cancellationToken);
            modelsWithoutJourney = orphanModels.Length;
            qualityIssues.AddRange(orphanModels.Select(model => new CommandCenterQualityIssueDto(
                "ModelWithoutJourney",
                $"{model.Code} - {model.Name}",
                "موديل نشط بلا رحلة مراحل نشطة مطلوبة.",
                null,
                null,
                null,
                model.Id,
                null)));
            modelsWithoutJourneyScopeNote = "محسوب لكل الموديلات النشطة على مستوى النظام.";
        }

        var workerById = attendanceWorkers.ToDictionary(worker => worker.Id);
        var workforce = new CommandCenterWorkforceDto
        {
            ActiveWorkers = attributionComplete ? attendanceWorkers.Length : null,
            PresentWorkers = presentWorkers,
            PresentPermanentlyAssignedWorkers = assignedPresentIds.Count,
            PresentUnassignedWorkers = attributionComplete ? presentUnassignedIds.Count : null,
            PermanentlyAssignedNotPresentWorkers = assignedNotPresentIds.Count,
            AssignmentCoverage = coverage,
            AttendanceEvidenceComplete = attendance.Count == attendanceWorkers.Length,
            AttributionNote = attributionComplete
                ? "الحاضر غير المسكن محسوب من كل العمال النشطين مقابل التسكين الدائم فقط."
                : "لا توجد علاقة موثوقة تربط العامل غير المسكن بقسم أو خط؛ لذلك لا يُنسب هذا العدد إلى النطاق المحدد.",
            PresentAssignedDetails = WorkerDetails(assignedPresentIds, workerById, attendance, assignments),
            PresentUnassignedDetails = attributionComplete
                ? WorkerDetails(presentUnassignedIds, workerById, attendance, [])
                : [],
            AssignedNotPresentDetails = WorkerDetails(assignedNotPresentIds, workerById, attendance, assignments)
        };

        var factoriesHierarchy = BuildHierarchy(
            factories,
            departments,
            commandLines,
            assignments,
            presentWorkerIds,
            query,
            operationStatus);
        var lineSummary = new CommandCenterLineSummaryDto(
            commandLines.Length,
            commandLines.Count(line => line.ReadinessStatus == "Ready"),
            commandLines.Count(line => line.ReadinessStatus == "StaffingShortage"),
            commandLines.Count(line => line.ReadinessStatus == "JourneyNotConfigured"),
            commandLines.Count(line => line.ReadinessStatus == "DataIncomplete"),
            commandLines.Count(IsProblemLine),
            commandLines.SelectMany(line => line.Operations).SelectMany(operation => operation.Stages)
                .Count(stage => stage.PresentPermanentlyAssignedWorkers == 0));
        var operationsSummary = new CommandCenterOperationsSummaryDto(
            commandOperations.Select(operation => operation.ProductionLineId).Distinct().Count(),
            commandLines.Count(line => line.Operations.Count == 0),
            commandOperations.Count(operation => operation.Status == "Draft"),
            commandOperations.Count(operation => operation.Status == "Approved"),
            commandOperations.Count(operation => operation.Status == "ApprovalCancelled"),
            commandOperations.Count(operation => operation.Status == "Cancelled"),
            commandOperations.Where(operation => operation.Status == "Approved").Sum(operation => operation.RecordedStageValue),
            commandOperations);
        var dataQuality = new CommandCenterDataQualityDto(
            qualityIssues.Count(issue => issue.Type == "MissingPrice"),
            qualityIssues.Count(issue => issue.Type == "MissingStandardTime"),
            qualityIssues.Count(issue => issue.Type == "StageWithoutPresentWorker"),
            modelsWithoutJourney,
            qualityIssues,
            modelsWithoutJourneyScopeNote);

        return Result<ManufacturingCommandCenterDto>.Success(new ManufacturingCommandCenterDto
        {
            Scope = new CommandCenterScopeDto(
                query.ProductionDate,
                query.FactoryId,
                query.DepartmentId,
                query.ProductionLineId,
                operationStatus,
                ScopeDescription(query, operationStatus)),
            FilterCatalog = new CommandCenterStructureCatalogDto(
                factories.Select(factory => new CommandCenterFactoryOptionDto(factory.Id, factory.Name, factory.Code)).ToArray(),
                departments.Select(department => new CommandCenterDepartmentOptionDto(department.Id, department.FactoryId, department.Name, department.Code)).ToArray(),
                catalogLines.Select(line => new CommandCenterLineOptionDto(line.Id, line.FactoryId, line.DepartmentId, line.Name, line.Code)).ToArray()),
            Workforce = workforce,
            LineSummary = lineSummary,
            Operations = operationsSummary,
            DataQuality = dataQuality,
            Factories = factoriesHierarchy,
            CalculatedAtUtc = calculatedAtUtc
        });
    }

    private static CommandCenterOperationDto BuildOperation(
        OperationRow operation,
        string status,
        IReadOnlyCollection<JourneyStageRow> journey,
        IReadOnlyCollection<ProductionRecordRow> records,
        IReadOnlyCollection<AssignmentRow> assignments,
        IReadOnlySet<Guid> presentWorkerIds,
        DateOnly productionDate,
        string scope)
    {
        var currentRecords = records.Where(record => record.Status != StageProductionRecordStatus.Cancelled).ToArray();
        var journeyStageIds = journey.Select(stage => stage.Id).ToHashSet();
        // A malformed or historical record for a stage outside this operation's current
        // journey cannot make completion exceed 100%.
        var registeredStageIds = currentRecords
            .Select(record => record.ProductModelStageId)
            .Where(journeyStageIds.Contains)
            .ToHashSet();
        var stages = journey.OrderBy(stage => stage.StageOrder).Select(stage =>
        {
            var stageAssignments = assignments.Where(assignment => assignment.SubStageId == stage.SubStageId).ToArray();
            var presentCount = stageAssignments.Where(assignment => presentWorkerIds.Contains(assignment.WorkerId))
                .Select(assignment => assignment.WorkerId).Distinct().Count();
            var alerts = new List<string>();
            if (stage.PiecePrice <= 0) alerts.Add("السعر غير مسجل.");
            if (!stage.StandardSeconds.HasValue || stage.StandardSeconds <= 0) alerts.Add("الزمن المعياري غير مسجل.");
            if (presentCount == 0) alerts.Add("لا يوجد عامل حاضر مسكن دائم.");
            else if (presentCount < stage.Capacity) alerts.Add($"الحضور يغطي {presentCount} من احتياج {stage.Capacity}.");
            if (!registeredStageIds.Contains(stage.Id)) alerts.Add("لم تُسجل المرحلة في تشغيل اليوم.");
            return new CommandCenterStageDto(
                stage.Id,
                stage.SubStageId,
                stage.MainStageName,
                stage.StageCode,
                stage.StageName,
                stage.StageOrder,
                stage.Capacity,
                stageAssignments.Select(assignment => assignment.WorkerId).Distinct().Count(),
                presentCount,
                stage.PiecePrice > 0,
                stage.StandardSeconds.HasValue && stage.StandardSeconds > 0,
                registeredStageIds.Contains(stage.Id),
                alerts);
        }).ToArray();
        var lastUpdate = new[]
            {
                operation.RecordedAtUtc,
                operation.UpdatedAtUtc,
                operation.ApprovedAtUtc ?? DateTime.MinValue,
                records.Select(record => record.CancelledAtUtc ?? record.ApprovedAtUtc ?? record.CreatedAtUtc)
                    .DefaultIfEmpty(DateTime.MinValue).Max()
            }
            .Max();
        return new CommandCenterOperationDto(
            operation.Id,
            operation.ProductionLineId,
            operation.ProductModelId,
            operation.ProductModelCode,
            operation.ProductModelName,
            status,
            operation.PlannedQuantity,
            currentRecords.Sum(record => record.TotalWorkerEarnings),
            registeredStageIds.Count,
            stages.Length,
            Ratio(registeredStageIds.Count, stages.Length, scope, productionDate, true),
            lastUpdate,
            stages);
    }

    private static List<CommandCenterQualityIssueDto> BuildQualityIssues(
        IReadOnlyCollection<CommandCenterLineDto> lines,
        IReadOnlyCollection<JourneyStageRow> journeyRows,
        IReadOnlyCollection<LineRow> lineRows)
    {
        var lineById = lineRows.ToDictionary(line => line.Id);
        var issues = new List<CommandCenterQualityIssueDto>();
        foreach (var stage in journeyRows)
        {
            var line = lineById[stage.ProductionLineId];
            if (stage.PiecePrice <= 0)
                issues.Add(Issue("MissingPrice", stage, line, "مرحلة موديل بلا سعر", $"{stage.MainStageName} / {stage.StageName}"));
            if (!stage.StandardSeconds.HasValue || stage.StandardSeconds <= 0)
                issues.Add(Issue("MissingStandardTime", stage, line, "مرحلة بلا زمن معياري", $"{stage.MainStageName} / {stage.StageName}"));
        }
        foreach (var line in lines)
        foreach (var operation in line.Operations)
        foreach (var stage in operation.Stages.Where(stage => stage.PresentPermanentlyAssignedWorkers == 0))
        {
            issues.Add(new CommandCenterQualityIssueDto(
                "StageWithoutPresentWorker",
                $"{line.Name} - {stage.StageName}",
                $"{operation.ProductModelCode}: مرحلة مطلوبة بلا عامل حاضر مسكن دائم.",
                line.FactoryId,
                line.DepartmentId,
                line.Id,
                operation.ProductModelId,
                stage.ProductModelStageId));
        }
        foreach (var line in lineRows.Where(line => line.DepartmentId is null))
        {
            issues.Add(new CommandCenterQualityIssueDto(
                "LineWithoutDepartment",
                line.Name,
                "خط نشط غير مربوط بقسم تشغيلي.",
                line.FactoryId,
                null,
                line.Id,
                null,
                null));
        }
        return issues;
    }

    private static CommandCenterQualityIssueDto Issue(
        string type,
        JourneyStageRow stage,
        LineRow line,
        string title,
        string detail) => new(
            type,
            $"{title}: {stage.StageCode}",
            detail,
            line.FactoryId,
            line.DepartmentId,
            line.Id,
            stage.ProductModelId,
            stage.Id);

    private static IReadOnlyCollection<CommandCenterFactoryDto> BuildHierarchy(
        IReadOnlyCollection<FactoryRow> factories,
        IReadOnlyCollection<DepartmentRow> departments,
        IReadOnlyCollection<CommandCenterLineDto> lines,
        IReadOnlyCollection<AssignmentRow> assignments,
        IReadOnlySet<Guid> presentWorkerIds,
        ManufacturingCommandCenterQuery query,
        string operationStatus) =>
        factories.Where(factory => FactoryMatchesScope(factory, departments, lines, query, operationStatus)).Select(factory =>
        {
            var factoryLines = lines.Where(line => line.FactoryId == factory.Id).ToArray();
            var factoryLineIds = factoryLines.Select(line => line.Id).ToHashSet();
            var factoryAssignments = assignments.Where(assignment => factoryLineIds.Contains(assignment.ProductionLineId)).ToArray();
            var factoryDepartments = departments.Where(department => department.FactoryId == factory.Id
                    && DepartmentMatchesScope(department, factoryLines, query, operationStatus))
                .Select(department => BuildDepartment(
                    department.Id,
                    department.Name,
                    department.Code,
                    factoryLines.Where(line => line.DepartmentId == department.Id).ToArray(),
                    factoryAssignments,
                    presentWorkerIds))
                .ToList();
            var withoutDepartment = factoryLines.Where(line => line.DepartmentId is null).ToArray();
            if (withoutDepartment.Length > 0)
                factoryDepartments.Add(BuildDepartment(null, "خطوط غير مربوطة بقسم", null, withoutDepartment, factoryAssignments, presentWorkerIds));
            return new CommandCenterFactoryDto(
                factory.Id,
                factory.Name,
                factory.Code,
                factoryDepartments.Count(department => department.Id.HasValue),
                factoryLines.Length,
                factoryAssignments.Where(assignment => presentWorkerIds.Contains(assignment.WorkerId))
                    .Select(assignment => assignment.WorkerId).Distinct().Count(),
                factoryLines.Count(IsProblemLine),
                factoryLines.SelectMany(line => line.Operations).Count(operation => operation.Status is "Draft" or "ApprovalCancelled"),
                factoryLines.SelectMany(line => line.Operations).Count(operation => operation.Status == "Approved"),
                factoryDepartments);
        }).ToArray();

    private static bool FactoryMatchesScope(
        FactoryRow factory,
        IReadOnlyCollection<DepartmentRow> departments,
        IReadOnlyCollection<CommandCenterLineDto> lines,
        ManufacturingCommandCenterQuery query,
        string operationStatus)
    {
        if (query.FactoryId.HasValue && factory.Id != query.FactoryId.Value) return false;
        if (query.DepartmentId.HasValue && !departments.Any(department => department.Id == query.DepartmentId.Value && department.FactoryId == factory.Id)) return false;
        if (query.ProductionLineId.HasValue && !lines.Any(line => line.Id == query.ProductionLineId.Value && line.FactoryId == factory.Id)) return false;
        return operationStatus == AllStatuses || lines.Any(line => line.FactoryId == factory.Id);
    }

    private static bool DepartmentMatchesScope(
        DepartmentRow department,
        IReadOnlyCollection<CommandCenterLineDto> factoryLines,
        ManufacturingCommandCenterQuery query,
        string operationStatus)
    {
        if (query.DepartmentId.HasValue && department.Id != query.DepartmentId.Value) return false;
        if (query.ProductionLineId.HasValue && !factoryLines.Any(line => line.Id == query.ProductionLineId.Value && line.DepartmentId == department.Id)) return false;
        return operationStatus == AllStatuses || factoryLines.Any(line => line.DepartmentId == department.Id);
    }

    private static CommandCenterDepartmentDto BuildDepartment(
        Guid? id,
        string name,
        string? code,
        IReadOnlyCollection<CommandCenterLineDto> lines,
        IReadOnlyCollection<AssignmentRow> assignments,
        IReadOnlySet<Guid> presentWorkerIds)
    {
        var lineIds = lines.Select(line => line.Id).ToHashSet();
        var scopedAssignments = assignments.Where(assignment => lineIds.Contains(assignment.ProductionLineId)).ToArray();
        return new(
            id,
            name,
            code,
            lines.Count,
            scopedAssignments.Where(assignment => presentWorkerIds.Contains(assignment.WorkerId)).Select(assignment => assignment.WorkerId).Distinct().Count(),
            scopedAssignments.Select(assignment => assignment.WorkerId).Distinct().Count(),
            null,
            lines.Count(line => line.ReadinessStatus == "Ready"),
            lines.Count(line => line.ReadinessStatus != "Ready"),
            lines.SelectMany(line => line.Operations).Count(operation => operation.Status is "Draft" or "ApprovalCancelled"),
            lines.SelectMany(line => line.Operations).Count(operation => operation.Status == "Approved"),
            "العامل الحاضر غير المسكن لا يمكن نسبه لقسم دون علاقة مصدر موثوقة.",
            lines);
    }

    private static IReadOnlyCollection<CommandCenterWorkerDetailDto> WorkerDetails(
        IReadOnlySet<Guid> workerIds,
        IReadOnlyDictionary<Guid, WorkerRow> workers,
        IReadOnlyDictionary<Guid, AttendanceStatusRecord> attendance,
        IReadOnlyCollection<AssignmentRow> assignments) =>
        workerIds.Where(workers.ContainsKey).Select(workerId =>
        {
            var worker = workers[workerId];
            return new CommandCenterWorkerDetailDto(
                worker.Id,
                worker.Code,
                worker.Name,
                attendance.TryGetValue(workerId, out var status) ? status.Status.ToString() : "NoRecord",
                assignments.Where(assignment => assignment.WorkerId == workerId)
                    .Select(assignment => assignment.StageName).Distinct().OrderBy(name => name).ToArray());
        }).OrderBy(worker => worker.WorkerName).ToArray();

    private static CommandCenterRatioDto Ratio(
        int numerator,
        int denominator,
        string scope,
        DateOnly date,
        bool attributable)
    {
        var percentage = attributable && denominator > 0
            ? Math.Round((decimal)numerator / denominator * 100m, 1, MidpointRounding.AwayFromZero)
            : (decimal?)null;
        return new CommandCenterRatioDto(
            numerator,
            denominator,
            percentage,
            scope,
            date,
            denominator == 0
                ? "NoData"
                : attributable ? "Calculated" : "NotAttributable");
    }

    private static bool LineMatchesOperationStatus(
        Guid lineId,
        string status,
        IReadOnlyCollection<OperationRow> operations,
        IReadOnlyDictionary<Guid, string> states)
    {
        var lineOperations = operations.Where(operation => operation.ProductionLineId == lineId).ToArray();
        return status == "None"
            ? lineOperations.Length == 0
            : lineOperations.Any(operation => states[operation.Id] == status);
    }

    private static string OperationState(OperationRow operation, IReadOnlyCollection<ProductionRecordRow> records)
    {
        if (operation.Status == ProductionOrderStatus.Completed) return "Approved";
        if (operation.Status == ProductionOrderStatus.Cancelled) return "Cancelled";
        if (operation.Status == ProductionOrderStatus.Draft && records.Any(record => record.CancelledAtUtc.HasValue))
            return "ApprovalCancelled";
        return "Draft";
    }

    private static bool IsProblemLine(CommandCenterLineDto line) =>
        line.ReadinessStatus != "Ready"
        || line.Operations.Count == 0
        || line.Operations.Any(operation => operation.Status is "Draft" or "ApprovalCancelled" or "Cancelled");

    private static string? NormalizeOperationStatus(string? value)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? AllStatuses : value.Trim();
        return SupportedOperationStatuses.FirstOrDefault(status => status.Equals(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static string ScopeDescription(ManufacturingCommandCenterQuery query, string operationStatus) =>
        $"date={query.ProductionDate:yyyy-MM-dd};factory={query.FactoryId?.ToString() ?? "all"};department={query.DepartmentId?.ToString() ?? "all"};line={query.ProductionLineId?.ToString() ?? "all"};operationStatus={operationStatus}";

    private sealed record FactoryRow(Guid Id, string Name, string Code);
    private sealed record DepartmentRow(Guid Id, Guid FactoryId, string Name, string Code);
    private sealed record LineRow(Guid Id, Guid FactoryId, Guid? DepartmentId, string Name, string? Code, DateTime UpdatedAtUtc);
    private sealed record WorkerRow(Guid Id, string Code, string Name);
    private sealed record AssignmentRow(Guid WorkerId, string WorkerCode, string WorkerName, Guid SubStageId, Guid ProductionLineId, DateTime UpdatedAtUtc, string StageName);
    private sealed record OperationRow(Guid Id, Guid ProductionLineId, Guid ProductModelId, string ProductModelCode, string ProductModelName, ProductionOrderStatus Status, decimal PlannedQuantity, DateTime RecordedAtUtc, DateTime UpdatedAtUtc, DateTime? ApprovedAtUtc);
    private sealed record ProductionRecordRow(Guid Id, Guid ProductionOrderId, Guid ProductModelStageId, StageProductionRecordStatus Status, decimal TotalWorkerEarnings, DateTime CreatedAtUtc, DateTime? ApprovedAtUtc, DateTime? CancelledAtUtc, string? ApprovalCancellationReason);
    private sealed record JourneyStageRow(Guid Id, Guid ProductModelId, Guid SubStageId, Guid ProductionLineId, string MainStageName, string StageCode, string StageName, int StageOrder, decimal PiecePrice, decimal? StandardSeconds, int Capacity, DateTime UpdatedAtUtc);
}
