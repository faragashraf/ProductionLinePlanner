using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

/// <summary>Read-only, batched projection for the daily attendance and staffing workspace.</summary>
public sealed class AttendanceWorkforceEngine(
    AppDbContext dbContext,
    IAttendanceEngine attendanceEngine,
    IAssignmentEngine assignmentEngine,
    ICairoTimeZoneProvider cairoTimeZoneProvider) : IAttendanceWorkforceEngine
{
    private const int ResolveBatchSize = 100;

    public async Task<Result<AttendanceWorkforcePageDto>> GetPageAsync(AttendanceWorkforceQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 10, 100);
        var (startUtc, endUtc) = GetDayBounds(query.ProductionDate);
        var workerQuery = dbContext.Workers.AsNoTracking();
        // A notification deep link is historical evidence. It must still resolve
        // its worker even if the worker became inactive after that production date.
        workerQuery = query.WorkerId.HasValue
            ? workerQuery.Where(worker => worker.Id == query.WorkerId.Value)
            : workerQuery.Where(worker => worker.IsActive && worker.EmploymentStatus == EmploymentStatus.Active);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            workerQuery = workerQuery.Where(worker => worker.FullName.Contains(term) || worker.EmployeeCode.Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(query.Department))
        {
            var department = query.Department.Trim();
            workerQuery = workerQuery.Where(worker => worker.LocalDepartmentName == department);
        }

        var attendanceDataAvailable = await dbContext.AttendanceRecords.AsNoTracking()
            .AnyAsync(record => record.AttendanceTimeUtc >= startUtc && record.AttendanceTimeUtc < endUtc, cancellationToken);
        var requiresResolvedFilters = query.FactoryId.HasValue || query.ProductionLineId.HasValue || query.MainStageId.HasValue || query.SubStageId.HasValue ||
            !string.IsNullOrWhiteSpace(query.AttendanceFilter) && query.AttendanceFilter != "all" ||
            !string.IsNullOrWhiteSpace(query.AssignmentFilter) && query.AssignmentFilter != "all" ||
            !string.IsNullOrWhiteSpace(query.OperationalFilter) || RequiresResolvedSort(query.SortBy);
        if (!requiresResolvedFilters)
        {
            var databaseTotal = await workerQuery.CountAsync(cancellationToken);
            var workers = await ApplyWorkerSort(workerQuery, query)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(worker => new WorkerHeader(worker.Id, worker.EmployeeCode, worker.FullName, worker.LocalDepartmentName, worker.PhotoReference)).ToListAsync(cancellationToken);
            var pageRows = await ResolveRowsAsync(workers, query.ProductionDate, startUtc, attendanceDataAvailable, cancellationToken);
            return Result<AttendanceWorkforcePageDto>.Success(new AttendanceWorkforcePageDto(query.ProductionDate, pageRows, BuildSummary(pageRows, attendanceDataAvailable) with { Scope = "current-page" }, page, pageSize, databaseTotal, Math.Max(1, (int)Math.Ceiling(databaseTotal / (double)pageSize))));
        }

        var candidateQuery = ApplyDatabaseCandidateFilters(workerQuery, query, startUtc, endUtc, attendanceDataAvailable);
        var orderedCandidates = ApplyWorkerSort(candidateQuery, query);
        var pageStart = ((long)page - 1L) * pageSize;
        var retainLimit = pageStart >= int.MaxValue ? 0 : (int)Math.Min(pageStart + pageSize, int.MaxValue);
        var retainedRows = new List<AttendanceWorkforceRowDto>(Math.Min(retainLimit, ResolveBatchSize));
        var summaryBuilder = new SummaryBuilder();
        var candidateOffset = 0;
        while (true)
        {
            var workers = await orderedCandidates.Skip(candidateOffset).Take(ResolveBatchSize)
                .Select(worker => new WorkerHeader(worker.Id, worker.EmployeeCode, worker.FullName, worker.LocalDepartmentName, worker.PhotoReference))
                .ToListAsync(cancellationToken);
            if (workers.Count == 0) break;
            candidateOffset += workers.Count;
            var batchRows = await ResolveRowsAsync(workers, query.ProductionDate, startUtc, attendanceDataAvailable, cancellationToken);
            var matchingRows = ApplyFilters(batchRows, query).ToArray();
            summaryBuilder.Add(matchingRows);
            if (retainLimit > 0)
            {
                retainedRows.AddRange(matchingRows);
                retainedRows = ApplySort(retainedRows, query).Take(retainLimit).ToList();
            }
            if (workers.Count < ResolveBatchSize) break;
        }

        var total = summaryBuilder.TotalWorkers;
        var summary = summaryBuilder.Build(attendanceDataAvailable);
        var items = pageStart >= int.MaxValue
            ? []
            : ApplySort(retainedRows, query).Skip((int)pageStart).Take(pageSize).ToArray();
        return Result<AttendanceWorkforcePageDto>.Success(new AttendanceWorkforcePageDto(query.ProductionDate, items, summary, page, pageSize, total, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize))));
    }

    private IQueryable<Worker> ApplyDatabaseCandidateFilters(
        IQueryable<Worker> workers,
        AttendanceWorkforceQuery query,
        DateTime startUtc,
        DateTime endUtc,
        bool attendanceDataAvailable)
    {
        if (query.FactoryId.HasValue || query.ProductionLineId.HasValue || query.MainStageId.HasValue || query.SubStageId.HasValue)
        {
            var matchingStageIds = dbContext.SubStages.AsNoTracking()
                .Where(stage => !query.SubStageId.HasValue || stage.Id == query.SubStageId.Value)
                .Where(stage => !query.MainStageId.HasValue || stage.MainStageId == query.MainStageId.Value)
                .Where(stage => !query.ProductionLineId.HasValue || dbContext.ProductionLines.Any(line => line.Id == query.ProductionLineId.Value && line.DepartmentId == stage.DepartmentId))
                .Where(stage => !query.FactoryId.HasValue || stage.MainStage!.Department!.FactoryId == query.FactoryId.Value)
                .Select(stage => stage.Id);
            workers = workers.Where(worker =>
                dbContext.WorkerDefaultAssignments.Any(assignment => assignment.WorkerId == worker.Id && assignment.IsActive
                    && (!query.ProductionLineId.HasValue || assignment.ProductionLineId == query.ProductionLineId.Value)
                    && (!query.FactoryId.HasValue || assignment.ProductionLine!.FactoryId == query.FactoryId.Value)
                    && matchingStageIds.Contains(assignment.SubStageId)) ||
                dbContext.WorkerTemporaryAssignments.Any(assignment => assignment.WorkerId == worker.Id && assignment.StartAtUtc <= startUtc && assignment.EndAtUtc > startUtc &&
                    (assignment.Status == "Active" || assignment.Status == "Scheduled") && matchingStageIds.Contains(assignment.ToSubStageId)));
        }

        var assignmentFilter = query.AssignmentFilter?.ToLowerInvariant();
        if (assignmentFilter is "assigned" or "unassigned" or "temporary")
        {
            var assignedWorkerIds = dbContext.WorkerDefaultAssignments.AsNoTracking().Where(assignment => assignment.IsActive).Select(assignment => assignment.WorkerId)
                .Concat(dbContext.WorkerTemporaryAssignments.AsNoTracking()
                    .Where(assignment => assignment.StartAtUtc <= startUtc && assignment.EndAtUtc > startUtc && (assignment.Status == "Active" || assignment.Status == "Scheduled"))
                    .Select(assignment => assignment.WorkerId));
            workers = assignmentFilter switch
            {
                "assigned" => workers.Where(worker => assignedWorkerIds.Contains(worker.Id)),
                "unassigned" => workers.Where(worker => !assignedWorkerIds.Contains(worker.Id)),
                "temporary" => workers.Where(worker => dbContext.WorkerTemporaryAssignments.Any(assignment => assignment.WorkerId == worker.Id && assignment.StartAtUtc <= startUtc && assignment.EndAtUtc > startUtc && (assignment.Status == "Active" || assignment.Status == "Scheduled"))),
                _ => workers
            };
        }

        var attendanceFilter = query.AttendanceFilter?.ToLowerInvariant();
        if (!attendanceDataAvailable && attendanceFilter == "needssync") return workers;
        if (attendanceDataAvailable && attendanceFilter == "needssync") return workers.Where(_ => false);
        if (attendanceFilter is "present" or "late" or "absent" or "incomplete" or "unassigned")
        {
            var attendanceRecords = dbContext.AttendanceRecords.AsNoTracking().Where(record => record.AttendanceTimeUtc >= startUtc && record.AttendanceTimeUtc < endUtc);
            workers = attendanceFilter switch
            {
                "present" => workers.Where(worker => attendanceRecords.Any(record => record.WorkerId == worker.Id && record.AttendanceStatus == AttendanceStatus.Present)),
                "late" => workers.Where(worker => attendanceRecords.Any(record => record.WorkerId == worker.Id && record.AttendanceStatus == AttendanceStatus.Late)),
                "absent" => workers.Where(worker => attendanceRecords.Any(record => record.WorkerId == worker.Id && record.AttendanceStatus == AttendanceStatus.Absent)),
                "incomplete" => workers.Where(worker => attendanceRecords.Any(record => record.WorkerId == worker.Id && (record.AttendanceStatus == AttendanceStatus.Present || record.AttendanceStatus == AttendanceStatus.Late))),
                "unassigned" => workers.Where(worker => !attendanceRecords.Any(record => record.WorkerId == worker.Id)),
                _ => workers
            };
        }
        return workers;
    }

    private static bool RequiresResolvedSort(string? sortBy) => !string.IsNullOrWhiteSpace(sortBy) && sortBy.ToLowerInvariant() is not ("name" or "code");

    private static IOrderedQueryable<Worker> ApplyWorkerSort(IQueryable<Worker> workers, AttendanceWorkforceQuery query)
    {
        var descending = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return query.SortBy?.ToLowerInvariant() == "code"
            ? descending ? workers.OrderByDescending(worker => worker.EmployeeCode).ThenByDescending(worker => worker.FullName) : workers.OrderBy(worker => worker.EmployeeCode).ThenBy(worker => worker.FullName)
            : descending ? workers.OrderByDescending(worker => worker.FullName).ThenByDescending(worker => worker.EmployeeCode) : workers.OrderBy(worker => worker.FullName).ThenBy(worker => worker.EmployeeCode);
    }

    private async Task<List<AttendanceWorkforceRowDto>> ResolveRowsAsync(IReadOnlyCollection<WorkerHeader> workers, DateOnly productionDate, DateTime asOfUtc, bool attendanceDataAvailable, CancellationToken cancellationToken)
    {
        var workerIds = workers.Select(worker => worker.Id).ToArray();
        var attendance = await attendanceEngine.GetPresenceWindowsByWorkerAsync(workerIds, productionDate, cancellationToken);
        if (attendance.IsFailure) throw new InvalidOperationException(attendance.Error!.Message);
        var assignments = await assignmentEngine.ResolveEffectiveAssignmentsAsync(workerIds, asOfUtc, cancellationToken);
        if (assignments.IsFailure) throw new InvalidOperationException(assignments.Error!.Message);
        var assignmentStates = assignments.Value!;
        var subStageIds = assignmentStates.Values.SelectMany(values => values).Select(value => value.EffectiveSubStageId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        var stages = await LoadStageHeadersAsync(assignmentStates.Values.SelectMany(values => values), subStageIds, cancellationToken);
        return workers.Select(worker => MapRow(worker, assignmentStates.GetValueOrDefault(worker.Id) ?? [], stages, attendance.Value!.GetValueOrDefault(worker.Id), attendanceDataAvailable)).ToList();
    }

    public async Task<Result<AttendanceWorkforceDetailDto>> GetWorkerDetailAsync(Guid workerId, DateOnly productionDate, CancellationToken cancellationToken = default)
    {
        // Daily attendance is historical evidence; a worker who later leaves
        // employment must remain readable from a notification deep link.
        var exists = await dbContext.Workers.AsNoTracking().AnyAsync(worker => worker.Id == workerId, cancellationToken);
        if (!exists) return Result<AttendanceWorkforceDetailDto>.Failure(new Error("NotFound", "Worker not found."));
        var (startUtc, endUtc) = GetDayBounds(productionDate);
        var assignmentResult = await assignmentEngine.ResolveEffectiveAssignmentsAsync([workerId], startUtc, cancellationToken);
        if (assignmentResult.IsFailure) return Result<AttendanceWorkforceDetailDto>.Failure(assignmentResult.Error!);
        var states = assignmentResult.Value![workerId];
        var ids = states.Select(state => state.EffectiveSubStageId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        var stages = await LoadStageHeadersAsync(states, ids, cancellationToken);
        // SQL Server datetime2 does not preserve DateTime.Kind. AttendanceTimeUtc is an
        // established UTC contract, so mark it explicitly before serializing it to clients.
        var attendanceRecords = await dbContext.AttendanceRecords.AsNoTracking()
            .Where(record => record.WorkerId == workerId && record.AttendanceTimeUtc >= startUtc && record.AttendanceTimeUtc < endUtc)
            .OrderBy(record => record.AttendanceTimeUtc)
            .Select(record => new AttendanceEvidence(record.AttendanceTimeUtc, record.SourcePayload))
            .ToArrayAsync(cancellationToken);
        return Result<AttendanceWorkforceDetailDto>.Success(new AttendanceWorkforceDetailDto(workerId, productionDate, MapAttendanceEvidence(attendanceRecords), MapAssignments(states, stages)));
    }

    private AttendanceWorkforceRowDto MapRow(WorkerHeader worker, IReadOnlyCollection<WorkerAssignmentState> states, IReadOnlyDictionary<(Guid SubStageId, Guid ProductionLineId), StageHeader> stages, AttendancePresenceWindowDto? attendance, bool dataAvailable)
    {
        var status = !dataAvailable ? "NeedsSync" : attendance is null ? "Unassigned" : attendance.Status switch
        {
            AttendanceStatus.Present => attendance.LastOutUtc.HasValue ? "Present" : "Incomplete",
            AttendanceStatus.Late => attendance.LastOutUtc.HasValue ? "Late" : "Incomplete",
            AttendanceStatus.Absent => "Absent",
            _ => "Unassigned"
        };
        var items = MapAssignments(states, stages);
        var isAssigned = items.Count > 0;
        var present = status is "Present" or "Late" or "Incomplete";
        return new AttendanceWorkforceRowDto(worker.Id, worker.EmployeeCode, worker.FullName, worker.DepartmentName, worker.PhotoReference, !string.IsNullOrWhiteSpace(worker.PhotoReference), status,
            attendance?.FirstInUtc, attendance?.LastOutUtc, dataAvailable && attendance is not null, status == "Incomplete", items, isAssigned,
            items.Any(item => item.AssignmentType is AssignmentType.Temporary or AssignmentType.Replacement), present && !isAssigned || status == "Absent" && isAssigned || status == "Incomplete");
    }

    private static IReadOnlyCollection<AttendanceWorkforceAssignmentDto> MapAssignments(IEnumerable<WorkerAssignmentState> states, IReadOnlyDictionary<(Guid SubStageId, Guid ProductionLineId), StageHeader> stages) => states
        .Where(state => state.AssignmentId.HasValue && state.AssignmentType.HasValue && state.EffectiveSubStageId.HasValue && state.ProductionLineId.HasValue && stages.ContainsKey((state.EffectiveSubStageId.Value, state.ProductionLineId.Value)))
        .Select(state => { var stage = stages[(state.EffectiveSubStageId!.Value, state.ProductionLineId!.Value)]; return new AttendanceWorkforceAssignmentDto(state.AssignmentId!.Value, state.AssignmentType!.Value, stage.Id, stage.MainStageId, stage.LineId, stage.FactoryId, stage.FactoryName, stage.LineName, stage.MainStageName, stage.Name, state.StartsAtUtc, state.EndsAtUtc, null); })
        .OrderBy(item => item.FactoryName).ThenBy(item => item.ProductionLineName).ThenBy(item => item.MainStageName).ThenBy(item => item.SubStageName).ToArray();

    private async Task<IReadOnlyDictionary<(Guid SubStageId, Guid ProductionLineId), StageHeader>> LoadStageHeadersAsync(
        IEnumerable<WorkerAssignmentState> states,
        Guid[] subStageIds,
        CancellationToken cancellationToken)
    {
        var lineIds = states.Where(state => state.ProductionLineId.HasValue).Select(state => state.ProductionLineId!.Value).Distinct().ToArray();
        var catalog = await dbContext.SubStages.AsNoTracking().Where(stage => subStageIds.Contains(stage.Id))
            .Select(stage => new { stage.Id, stage.Name, stage.MainStageId, MainStageName = stage.MainStage!.Name, stage.DepartmentId })
            .ToArrayAsync(cancellationToken);
        var lines = await dbContext.ProductionLines.AsNoTracking().Where(line => lineIds.Contains(line.Id))
            .Select(line => new { line.Id, line.Name, line.DepartmentId, line.FactoryId, FactoryName = line.Factory!.Name })
            .ToArrayAsync(cancellationToken);
        return (from stage in catalog
                join line in lines on stage.DepartmentId equals line.DepartmentId
                select new StageHeader(stage.Id, stage.Name, stage.MainStageId, stage.MainStageName, line.Id, line.Name, line.FactoryId, line.FactoryName))
            .ToDictionary(stage => (stage.Id, stage.LineId));
    }

    private static IEnumerable<AttendanceWorkforceRowDto> ApplyFilters(IEnumerable<AttendanceWorkforceRowDto> rows, AttendanceWorkforceQuery query)
    {
        if (query.FactoryId.HasValue) rows = rows.Where(row => row.Assignments.Any(item => item.FactoryId == query.FactoryId.Value));
        if (query.ProductionLineId.HasValue) rows = rows.Where(row => row.Assignments.Any(item => item.ProductionLineId == query.ProductionLineId.Value));
        if (query.MainStageId.HasValue) rows = rows.Where(row => row.Assignments.Any(item => item.MainStageId == query.MainStageId.Value));
        if (query.SubStageId.HasValue) rows = rows.Where(row => row.Assignments.Any(item => item.SubStageId == query.SubStageId.Value));
        if (!string.IsNullOrWhiteSpace(query.AttendanceFilter) && query.AttendanceFilter != "all") rows = rows.Where(row => string.Equals(row.AttendanceStatus, query.AttendanceFilter, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.AssignmentFilter) && query.AssignmentFilter != "all") rows = query.AssignmentFilter switch
        {
            "assigned" => rows.Where(row => row.IsAssigned), "unassigned" => rows.Where(row => !row.IsAssigned), "temporary" => rows.Where(row => row.HasTemporaryAssignment), "multiple" => rows.Where(row => row.Assignments.Count > 1), _ => rows
        };
        if (!string.IsNullOrWhiteSpace(query.OperationalFilter)) rows = query.OperationalFilter switch
        {
            "present-unassigned" => rows.Where(row => row.AttendanceStatus is "Present" or "Late" && !row.IsAssigned),
            "assigned-absent" => rows.Where(row => row.AttendanceStatus == "Absent" && row.IsAssigned),
            "review" => rows.Where(row => row.NeedsReview), _ => rows
        };
        return rows;
    }

    private static IEnumerable<AttendanceWorkforceRowDto> ApplySort(IEnumerable<AttendanceWorkforceRowDto> rows, AttendanceWorkforceQuery query)
    {
        var descending = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return query.SortBy?.ToLowerInvariant() switch
        {
            "name" => descending
                ? rows.OrderByDescending(row => row.FullName).ThenByDescending(row => row.EmployeeCode)
                : rows.OrderBy(row => row.FullName).ThenBy(row => row.EmployeeCode),
            "code" => descending ? rows.OrderByDescending(row => row.EmployeeCode) : rows.OrderBy(row => row.EmployeeCode),
            "checkin" => descending ? rows.OrderByDescending(row => row.FirstCheckInUtc) : rows.OrderBy(row => row.FirstCheckInUtc),
            "attendance" => descending ? rows.OrderByDescending(row => row.AttendanceStatus) : rows.OrderBy(row => row.AttendanceStatus),
            _ => rows.OrderByDescending(row => row.NeedsReview).ThenBy(row => row.FullName).ThenBy(row => row.EmployeeCode)
        };
    }

    private static AttendanceWorkforceSummaryDto BuildSummary(IReadOnlyCollection<AttendanceWorkforceRowDto> rows, bool available) => new(
        rows.Count, rows.Count(row => row.AttendanceStatus == "Present"), rows.Count(row => row.AttendanceStatus == "Absent"), rows.Count(row => row.AttendanceStatus == "Late"), rows.Count(row => row.HasSinglePunch),
        rows.Count(row => row.AttendanceStatus is "Present" or "Late" && !row.IsAssigned), rows.Count(row => row.AttendanceStatus == "Absent" && row.IsAssigned), rows.Count(row => row.NeedsReview), available, "filtered-results");

    private sealed class SummaryBuilder
    {
        public int TotalWorkers { get; private set; }
        private int PresentWorkers { get; set; }
        private int AbsentWorkers { get; set; }
        private int LateWorkers { get; set; }
        private int IncompleteWorkers { get; set; }
        private int UnassignedPresentWorkers { get; set; }
        private int AssignedAbsentWorkers { get; set; }
        private int ReviewRequiredWorkers { get; set; }

        public void Add(IEnumerable<AttendanceWorkforceRowDto> rows)
        {
            foreach (var row in rows)
            {
                TotalWorkers++;
                if (row.AttendanceStatus == "Present") PresentWorkers++;
                if (row.AttendanceStatus == "Absent") AbsentWorkers++;
                if (row.AttendanceStatus == "Late") LateWorkers++;
                if (row.HasSinglePunch) IncompleteWorkers++;
                if (row.AttendanceStatus is "Present" or "Late" && !row.IsAssigned) UnassignedPresentWorkers++;
                if (row.AttendanceStatus == "Absent" && row.IsAssigned) AssignedAbsentWorkers++;
                if (row.NeedsReview) ReviewRequiredWorkers++;
            }
        }

        public AttendanceWorkforceSummaryDto Build(bool available) => new(TotalWorkers, PresentWorkers, AbsentWorkers, LateWorkers, IncompleteWorkers, UnassignedPresentWorkers, AssignedAbsentWorkers, ReviewRequiredWorkers, available, "filtered-results");
    }

    private (DateTime StartUtc, DateTime EndUtc) GetDayBounds(DateOnly date)
    {
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return (TimeZoneInfo.ConvertTimeToUtc(start, cairoTimeZoneProvider.TimeZone), TimeZoneInfo.ConvertTimeToUtc(start.AddDays(1), cairoTimeZoneProvider.TimeZone));
    }

    private static IReadOnlyCollection<AttendanceWorkforcePunchDto> MapAttendanceEvidence(IEnumerable<AttendanceEvidence> records)
    {
        var punches = new List<DateTime>();
        foreach (var record in records)
        {
            var parsed = false;
            if (!string.IsNullOrWhiteSpace(record.SourcePayload))
            {
                try
                {
                    using var json = JsonDocument.Parse(record.SourcePayload);
                    if (json.RootElement.TryGetProperty("FirstInUtc", out var first) && first.TryGetDateTime(out var firstValue))
                    {
                        punches.Add(EnsureUtc(firstValue));
                        parsed = true;
                    }
                    if (json.RootElement.TryGetProperty("LastOutUtc", out var last) && last.ValueKind != JsonValueKind.Null && last.TryGetDateTime(out var lastValue))
                    {
                        punches.Add(EnsureUtc(lastValue));
                    }
                }
                catch (JsonException)
                {
                    // Legacy payloads still have a UTC attendance record to display.
                }
            }
            if (!parsed) punches.Add(EnsureUtc(record.AttendanceTimeUtc));
        }

        // ZKTime CHECKTYPE is not retained as a trustworthy domain field. Do not infer
        // an in/out direction from order; present neutral attendance evidence instead.
        return punches.Distinct().OrderBy(value => value)
            .Select(value => new AttendanceWorkforcePunchDto(value, "Punch"))
            .ToArray();
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed record WorkerHeader(Guid Id, string EmployeeCode, string FullName, string? DepartmentName, string? PhotoReference);
    private sealed record StageHeader(Guid Id, string Name, Guid MainStageId, string MainStageName, Guid LineId, string LineName, Guid FactoryId, string FactoryName);
    private sealed record AttendanceEvidence(DateTime AttendanceTimeUtc, string? SourcePayload);
}
