using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

public sealed class ManufacturingCommandCenterEngineTests
{
    [Fact]
    public async Task Workforce_uses_present_or_late_and_counts_each_permanently_assigned_worker_once()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Engine.GetAsync(new(fixture.Date));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Workforce.PresentWorkers);
        Assert.Equal(1, result.Value.Workforce.PresentPermanentlyAssignedWorkers);
        Assert.Equal(1, result.Value.Workforce.PresentUnassignedWorkers);
        Assert.Equal(1, result.Value.Workforce.PermanentlyAssignedNotPresentWorkers);
        Assert.Equal(1, result.Value.Workforce.AssignmentCoverage.Numerator);
        Assert.Equal(2, result.Value.Workforce.AssignmentCoverage.Denominator);
        Assert.Equal(50m, result.Value.Workforce.AssignmentCoverage.Percentage);
        Assert.Single(result.Value.Workforce.PresentAssignedDetails);
    }

    [Fact]
    public async Task Line_rules_cover_no_operation_draft_approved_multi_line_model_and_missing_stage_data()
    {
        await using var fixture = await Fixture.CreateAsync();

        var data = (await fixture.Engine.GetAsync(new(fixture.Date))).Value!;

        Assert.Equal(3, data.LineSummary.ActiveLines);
        Assert.Equal(1, data.Operations.DraftOperations);
        Assert.Equal(1, data.Operations.ApprovedOperations);
        Assert.Equal(1, data.Operations.LinesWithoutOperation);
        Assert.Equal(1, data.DataQuality.ModelStagesWithoutPrice);
        Assert.Equal(1, data.DataQuality.ModelStagesWithoutStandardTime);
        Assert.Equal(1, data.DataQuality.ActiveJourneyStagesWithoutPresentWorker);
        Assert.Equal(1, data.DataQuality.ActiveModelsWithoutJourney);
        var unconfiguredLine = Assert.Single(
            data.Factories.SelectMany(factory => factory.Departments).SelectMany(department => department.Lines),
            line => line.ReadinessStatus == "JourneyNotConfigured");
        Assert.Equal("Line C", unconfiguredLine.Name);
    }

    [Fact]
    public async Task Production_uses_order_line_quantity_and_never_sums_stage_quantities()
    {
        await using var fixture = await Fixture.CreateAsync();

        var data = (await fixture.Engine.GetAsync(new(fixture.Date))).Value!;
        var draft = Assert.Single(data.Operations.Items, item => item.Status == "Draft");
        var approved = Assert.Single(data.Operations.Items, item => item.Status == "Approved");

        Assert.Equal(100m, draft.FinalLineQuantity);
        Assert.Equal(25m, draft.RecordedStageValue);
        Assert.Equal(50m, draft.StageRegistrationCoverage.Percentage);
        Assert.Equal(50m, approved.FinalLineQuantity);
        Assert.Equal(10m, data.Operations.ApprovedRecordedValue);
    }

    [Fact]
    public async Task Filters_apply_to_summary_hierarchy_and_operations_together()
    {
        await using var fixture = await Fixture.CreateAsync();

        var data = (await fixture.Engine.GetAsync(new(
            fixture.Date,
            fixture.Factory.Id,
            fixture.Department.Id,
            fixture.LineB.Id,
            "Approved"))).Value!;

        Assert.Equal(1, data.LineSummary.ActiveLines);
        Assert.Equal(1, data.Operations.ApprovedOperations);
        Assert.Single(data.Factories);
        Assert.Single(data.Factories.Single().Departments.Single().Lines);
        Assert.Equal(fixture.LineB.Id, data.Factories.Single().Departments.Single().Lines.Single().Id);
        Assert.Null(data.Workforce.PresentUnassignedWorkers);
        Assert.Null(data.Workforce.AssignmentCoverage.Percentage);
    }

    [Fact]
    public async Task Operation_status_filter_excludes_other_operation_states_on_the_same_line()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddApprovedOperationToLineAAsync();

        var data = (await fixture.Engine.GetAsync(new(fixture.Date, OperationStatus: "Draft"))).Value!;

        Assert.Equal(1, data.LineSummary.ActiveLines);
        Assert.Equal(1, data.Operations.DraftOperations);
        Assert.Equal(0, data.Operations.ApprovedOperations);
        Assert.All(data.Operations.Items, operation => Assert.Equal("Draft", operation.Status));
        Assert.All(
            data.Factories.SelectMany(factory => factory.Departments).SelectMany(department => department.Lines)
                .SelectMany(line => line.Operations),
            operation => Assert.Equal("Draft", operation.Status));
    }

    [Fact]
    public async Task Active_department_without_lines_remains_in_the_real_hierarchy()
    {
        await using var fixture = await Fixture.CreateAsync();
        var emptyDepartment = new Department(Guid.NewGuid(), fixture.Factory.Id, "EMPTY", "قسم بلا خطوط", null, 2, createdAtUtc: DateTime.UtcNow);
        fixture.Db.Departments.Add(emptyDepartment);
        await fixture.Db.SaveChangesAsync();

        var data = (await fixture.Engine.GetAsync(new(fixture.Date))).Value!;
        var department = Assert.Single(data.Factories.Single(factory => factory.Id == fixture.Factory.Id).Departments,
            item => item.Id == emptyDepartment.Id);

        Assert.Equal(0, department.ActiveLines);
        Assert.Empty(department.Lines);
    }

    [Fact]
    public async Task Inactive_departments_and_their_lines_are_excluded_from_catalog_and_metrics()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Department.Deactivate(fixture.Date.ToDateTime(new TimeOnly(8, 0)));
        await fixture.Db.SaveChangesAsync();

        var data = (await fixture.Engine.GetAsync(new(fixture.Date))).Value!;

        Assert.Empty(data.FilterCatalog.Departments);
        Assert.Empty(data.FilterCatalog.Lines);
        Assert.Equal(0, data.LineSummary.ActiveLines);
        Assert.Empty(data.Factories.Single(factory => factory.Id == fixture.Factory.Id).Departments);
    }

    [Fact]
    public async Task Zero_attendance_denominator_returns_no_percentage()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        var engine = new ManufacturingCommandCenterEngine(db, new AttendanceStub(new Dictionary<Guid, AttendanceStatusRecord>()));

        var data = (await engine.GetAsync(new(new DateOnly(2026, 7, 22)))).Value!;

        Assert.Equal(0, data.Workforce.AssignmentCoverage.Denominator);
        Assert.Null(data.Workforce.AssignmentCoverage.Percentage);
        Assert.Equal("NoData", data.Workforce.AssignmentCoverage.ZeroBehavior);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            AppDbContext db,
            ManufacturingCommandCenterEngine engine,
            DateOnly date,
            Factory factory,
            Department department,
            ProductionLine lineA,
            ProductionLine lineB,
            ProductModel model,
            ProductModelStage stageA1,
            SubStage subA1,
            Guid actor,
            DateTime now)
        {
            Db = db; Engine = engine; Date = date; Factory = factory; Department = department; LineA = lineA; LineB = lineB;
            this.model = model; this.stageA1 = stageA1; this.subA1 = subA1; this.actor = actor; this.now = now;
        }

        public AppDbContext Db { get; }
        public ManufacturingCommandCenterEngine Engine { get; }
        public DateOnly Date { get; }
        public Factory Factory { get; }
        public Department Department { get; }
        public ProductionLine LineA { get; }
        public ProductionLine LineB { get; }
        private readonly ProductModel model;
        private readonly ProductModelStage stageA1;
        private readonly SubStage subA1;
        private readonly Guid actor;
        private readonly DateTime now;

        public static async Task<Fixture> CreateAsync()
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
            var now = new DateTime(2026, 7, 22, 8, 0, 0, DateTimeKind.Utc);
            var date = new DateOnly(2026, 7, 22);
            var actor = Guid.NewGuid();
            var factory = new Factory(Guid.NewGuid(), "Factory", "FAC", createdAtUtc: now);
            var department = new Department(Guid.NewGuid(), factory.Id, "DEP", "القسم", null, 1, createdAtUtc: now);
            var lineA = new ProductionLine(Guid.NewGuid(), factory.Id, "Line A", 1, "A", createdAtUtc: now, departmentId: department.Id);
            var lineB = new ProductionLine(Guid.NewGuid(), factory.Id, "Line B", 2, "B", createdAtUtc: now, departmentId: department.Id);
            var lineC = new ProductionLine(Guid.NewGuid(), factory.Id, "Line C", 3, "C", createdAtUtc: now, departmentId: department.Id);
            var mainA = new MainStage(Guid.NewGuid(), department.Id, "Main A", 1, createdAtUtc: now);
            var mainB = new MainStage(Guid.NewGuid(), department.Id, "Main B", 2, createdAtUtc: now);
            var mainC = new MainStage(Guid.NewGuid(), department.Id, "Main C", 3, createdAtUtc: now);
            var subA1 = new SubStage(Guid.NewGuid(), mainA.Id, "Stage A1", "A1", 2, 1, createdAtUtc: now);
            var subA2 = new SubStage(Guid.NewGuid(), mainA.Id, "Stage A2", "A2", 1, 2, createdAtUtc: now);
            var subB = new SubStage(Guid.NewGuid(), mainB.Id, "Stage B", "B1", 1, 1, createdAtUtc: now);
            var subC = new SubStage(Guid.NewGuid(), mainC.Id, "Stage C", "C1", 1, 1, createdAtUtc: now);
            var model = new ProductModel(Guid.NewGuid(), "M-1", "Model 1", createdAtUtc: now);
            var orphanModel = new ProductModel(Guid.NewGuid(), "M-ORPHAN", "Orphan", createdAtUtc: now);
            var stageA1 = new ProductModelStage(Guid.NewGuid(), model.Id, lineA.Id, subA1.Id, 1, 0m, null, CompensationMode.SharedPercentage, createdAtUtc: now);
            var stageA2 = new ProductModelStage(Guid.NewGuid(), model.Id, lineA.Id, subA2.Id, 2, 2m, 10m, CompensationMode.SharedPercentage, createdAtUtc: now);
            var stageB = new ProductModelStage(Guid.NewGuid(), model.Id, lineB.Id, subB.Id, 3, 1m, 10m, CompensationMode.SharedPercentage, createdAtUtc: now);
            var assignedPresent = new Worker(Guid.NewGuid(), "W-1", "Present assigned", createdAtUtc: now);
            var unassignedPresent = new Worker(Guid.NewGuid(), "W-2", "Present unassigned", createdAtUtc: now);
            var assignedAbsent = new Worker(Guid.NewGuid(), "W-3", "Assigned absent", createdAtUtc: now);
            db.AddRange(factory, department, lineA, lineB, lineC, mainA, mainB, mainC, subA1, subA2, subB, subC, model, orphanModel, stageA1, stageA2, stageB, assignedPresent, unassignedPresent, assignedAbsent);
            db.AddRange(
                new WorkerDefaultAssignment(Guid.NewGuid(), assignedPresent.Id, subA1.Id, actor, now, productionLineId: lineA.Id),
                new WorkerDefaultAssignment(Guid.NewGuid(), assignedPresent.Id, subB.Id, actor, now, productionLineId: lineB.Id),
                new WorkerDefaultAssignment(Guid.NewGuid(), assignedAbsent.Id, subA1.Id, actor, now, productionLineId: lineA.Id));

            var draft = new ProductionOrder(Guid.NewGuid(), "DLY-A", model.Id, lineA.Id, date, 100m, null, actor, now);
            draft.MarkDailyOperation("DailyProductionOperations/test-a", now);
            var draftRecord = Record(draft, stageA1, factory, lineA, model, subA1, 100m, actor, now);
            var draftAllocation = new StageProductionWorkerAllocation(Guid.NewGuid(), assignedPresent.Id, assignedPresent.EmployeeCode, assignedPresent.FullName, 100m, null, null);
            draftAllocation.SetCalculatedAmounts(100m, 25m);
            draftRecord.ReplaceAllocations([draftAllocation]);

            var approved = new ProductionOrder(Guid.NewGuid(), "DLY-B", model.Id, lineB.Id, date, 50m, null, actor, now);
            approved.MarkDailyOperation("DailyProductionOperations/test-b", now);
            var approvedRecord = Record(approved, stageB, factory, lineB, model, subB, 50m, actor, now);
            var approvedAllocation = new StageProductionWorkerAllocation(Guid.NewGuid(), assignedPresent.Id, assignedPresent.EmployeeCode, assignedPresent.FullName, 100m, null, null);
            approvedAllocation.SetCalculatedAmounts(50m, 10m);
            approvedRecord.ReplaceAllocations([approvedAllocation]);
            approvedRecord.Approve(actor, now.AddMinutes(5));
            approved.ApproveDay(actor, now.AddMinutes(5));
            db.AddRange(draft, draftRecord, approved, approvedRecord);
            await db.SaveChangesAsync();

            var attendance = new Dictionary<Guid, AttendanceStatusRecord>
            {
                [assignedPresent.Id] = new(assignedPresent.Id, AttendanceStatus.Present, now, "test"),
                [unassignedPresent.Id] = new(unassignedPresent.Id, AttendanceStatus.Late, now, "test"),
                [assignedAbsent.Id] = new(assignedAbsent.Id, AttendanceStatus.Absent, now, "test")
            };
            return new Fixture(db, new ManufacturingCommandCenterEngine(db, new AttendanceStub(attendance)), date, factory, department, lineA, lineB, model, stageA1, subA1, actor, now);
        }

        public async Task AddApprovedOperationToLineAAsync()
        {
            var order = new ProductionOrder(Guid.NewGuid(), "DLY-A-APPROVED", model.Id, LineA.Id, Date, 40m, null, actor, now.AddMinutes(10));
            order.MarkDailyOperation("DailyProductionOperations/test-a-approved", now.AddMinutes(10));
            var record = Record(order, stageA1, Factory, LineA, model, subA1, 40m, actor, now.AddMinutes(10));
            record.Approve(actor, now.AddMinutes(11));
            order.ApproveDay(actor, now.AddMinutes(11));
            Db.AddRange(order, record);
            await Db.SaveChangesAsync();
        }

        private static StageProductionRecord Record(ProductionOrder order, ProductModelStage stage, Factory factory, ProductionLine line, ProductModel model, SubStage subStage, decimal quantity, Guid actor, DateTime now) =>
            new(Guid.NewGuid(), order.Id, stage.Id, order.ProductionDate, quantity, quantity, 0m, subStage.Code, subStage.Name, stage.PiecePrice, stage.StandardSeconds, stage.CompensationMode, model.Code, model.Name, factory.Code, factory.Name, line.LineCode ?? line.Name, line.Name, "Main", Guid.NewGuid(), null, actor, now);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class AttendanceStub(IReadOnlyDictionary<Guid, AttendanceStatusRecord> statuses) : IAttendanceEngine
    {
        public Task<Result<Dictionary<Guid, AttendanceStatusRecord>>> GetLatestAttendanceStatusByWorkerAsync(IEnumerable<Guid> workerIds, DateTime? asOfUtc = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<Dictionary<Guid, AttendanceStatusRecord>>.Success(workerIds.Distinct().Where(statuses.ContainsKey).ToDictionary(id => id, id => statuses[id])));
        public Task<Result<AttendanceWorkerStateDto[]>> GetTodayAttendanceAsync(Guid? factoryId, Guid? lineId, DateTime? dateUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AttendanceRecordDto[]>> GetWorkerAttendanceAsync(Guid workerId, DateTime? fromDateUtc, DateTime? toDateUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AttendanceSubStageAttendanceDto>> GetSubStageAttendanceAsync(Guid subStageId, DateTime? dateUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AttendanceSyncResultDto>> SyncTodayAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AttendanceSyncResultDto>> SyncForProductionDateAsync(DateOnly productionDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<Dictionary<Guid, AttendancePresenceWindowDto>>> GetPresenceWindowsByWorkerAsync(IEnumerable<Guid> workerIds, DateOnly productionDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
