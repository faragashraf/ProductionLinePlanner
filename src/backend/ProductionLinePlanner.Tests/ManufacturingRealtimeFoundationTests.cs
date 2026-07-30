using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using ProductionLinePlanner.Api.Realtime;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Realtime;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Infrastructure.Realtime;

namespace ProductionLinePlanner.Tests;

public sealed class ManufacturingRealtimeFoundationTests
{
    [Fact]
    public async Task Worker_batch_publishes_one_compact_invalidation_after_successful_save()
    {
        var publisher = new RecordingPublisher();
        await using var db = CreateDb(publisher, Guid.NewGuid());
        db.Workers.AddRange(Enumerable.Range(1, 50)
            .Select(index => new Worker(Guid.NewGuid(), $"W-{index:000}", $"Worker {index}", index.ToString(), index.ToString())));

        await db.SaveChangesAsync();

        var change = Assert.Single(publisher.Changes);
        Assert.Equal(ManufacturingEntityType.Worker, change.EntityType);
        Assert.Equal(ManufacturingChangeType.Updated, change.ChangeType);
        Assert.Equal(Guid.Empty, change.EntityId);
        Assert.Null(change.WorkerId);
        Assert.Equal(50, change.WorkerIds?.Count);
        Assert.Equal(["created"], change.WorkerChangeKinds);
    }

    [Fact]
    public async Task Attendance_batch_publishes_one_compact_invalidation_and_empty_save_publishes_none()
    {
        var publisher = new RecordingPublisher();
        await using var db = CreateDb(publisher, Guid.NewGuid());
        var workers = Enumerable.Range(1, 20)
            .Select(index => new Worker(Guid.NewGuid(), $"A-{index:000}", $"Worker {index}"))
            .ToArray();
        db.Workers.AddRange(workers);
        await db.SaveChangesAsync();
        publisher.Changes.Clear();
        var records = workers.Select((worker, index) => new AttendanceRecord(
            Guid.NewGuid(), worker.Id, DateTime.UtcNow.AddMinutes(index), AttendanceStatus.Present, "staging")).ToArray();
        db.AttendanceRecords.AddRange(records);

        await db.SaveChangesAsync();

        var change = Assert.Single(publisher.Changes);
        Assert.Equal(ManufacturingEntityType.AttendanceRecord, change.EntityType);
        Assert.Equal(Guid.Empty, change.EntityId);
        Assert.Null(change.WorkerId);
        Assert.Equal(20, change.AddedAttendanceCount);
        Assert.Equal(0, change.UpdatedAttendanceCount);
        Assert.Equal(20, change.WorkerIds?.Count);
        Assert.Single(change.AffectedAttendanceDates ?? []);
        Assert.Equal(["created"], change.AttendanceChangeKinds);
        Assert.Equal("manufacturing.attendance.changed", ManufacturingDataChangedMessage.From(change).EventType);
        publisher.Changes.Clear();

        await db.SaveChangesAsync();

        Assert.Empty(publisher.Changes);

        records[0].UpdateAttendanceStatus(
            records[0].AttendanceTimeUtc,
            AttendanceStatus.Absent,
            "staging");
        await db.SaveChangesAsync();

        var updated = Assert.Single(publisher.Changes);
        Assert.Equal(0, updated.AddedAttendanceCount);
        Assert.Equal(1, updated.UpdatedAttendanceCount);
        Assert.Equal(["updated"], updated.AttendanceChangeKinds);
    }

    [Fact]
    public async Task Failed_worker_batch_save_publishes_no_invalidation()
    {
        var publisher = new RecordingPublisher();
        var coordinator = new ManufacturingDataChangeTransactionCoordinator(
            publisher,
            NullLogger<ManufacturingDataChangeTransactionCoordinator>.Instance);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(
                new ManufacturingDataChangeSaveChangesInterceptor(
                    publisher,
                    new CurrentUserStub(Guid.NewGuid()),
                    new CorrelationStub(),
                    coordinator,
                    NullLogger<ManufacturingDataChangeSaveChangesInterceptor>.Instance),
                new ThrowOnSavingWorkerInterceptor())
            .Options;
        await using var db = new AppDbContext(options);
        db.Workers.AddRange(
            new Worker(Guid.NewGuid(), "W-001", "Worker 1"),
            new Worker(Guid.NewGuid(), "W-002", "Worker 2"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        Assert.Empty(publisher.Changes);
    }

    [Fact]
    public async Task Successful_master_data_save_publishes_a_compact_event_after_commit()
    {
        var publisher = new RecordingPublisher();
        var actor = Guid.NewGuid();
        await using var db = CreateDb(publisher, actor);
        var model = new ProductModel(Guid.NewGuid(), "M-001", "Model");

        db.ProductModels.Add(model);
        await db.SaveChangesAsync();

        var change = Assert.Single(publisher.Changes);
        Assert.Equal(ManufacturingEntityType.ProductModel, change.EntityType);
        Assert.Equal(ManufacturingChangeType.Created, change.ChangeType);
        Assert.Equal(model.Id, change.EntityId);
        Assert.Equal(actor, change.ActorUserId);
        Assert.Equal(model.Id, change.ProductModelId);
        Assert.Null(change.CorrelationId);
    }

    [Fact]
    public async Task Daily_production_order_save_publishes_one_contextual_event_after_successful_save()
    {
        var publisher = new RecordingPublisher();
        var actor = Guid.NewGuid();
        await using var db = CreateDb(publisher, actor);
        var factory = new Factory(Guid.NewGuid(), "Factory", "F-001");
        var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1, "L-001");
        var model = new ProductModel(Guid.NewGuid(), "M-001", "Model");
        db.AddRange(factory, line, model);
        await db.SaveChangesAsync();
        publisher.Changes.Clear();

        var date = new DateOnly(2026, 7, 21);
        var order = new ProductionOrder(Guid.NewGuid(), "DLY-001", model.Id, line.Id, date, 10m, null, actor, DateTime.UtcNow);
        order.MarkDailyOperation("DailyProductionOperations/test", DateTime.UtcNow);
        db.Add(order);
        await db.SaveChangesAsync();

        var change = Assert.Single(publisher.Changes);
        Assert.Equal(ManufacturingEntityType.ProductionOrder, change.EntityType);
        Assert.Equal(ManufacturingChangeType.Created, change.ChangeType);
        Assert.Equal(order.Id, change.EntityId);
        Assert.Equal(factory.Id, change.FactoryId);
        Assert.Equal(line.Id, change.ProductionLineId);
        Assert.Equal(model.Id, change.ProductModelId);
        Assert.Equal(date, change.ProductionDate);
        Assert.Equal(["manufacturing:daily-production-operations", "manufacturing:manufacturing-command-center", "manufacturing:reports"], ManufacturingRealtimeGroups.ForChange(change));
    }

    [Fact]
    public async Task Worker_department_assignment_publishes_relationship_scope_for_old_and_new_departments()
    {
        var publisher = new RecordingPublisher();
        await using var db = CreateDb(publisher, Guid.NewGuid());
        var factory = new Factory(Guid.NewGuid(), "Factory", "F-001");
        var oldDepartment = new Department(Guid.NewGuid(), factory.Id, "D-001", "Old", null, 1);
        var newDepartment = new Department(Guid.NewGuid(), factory.Id, "D-002", "New", null, 2);
        var worker = new Worker(Guid.NewGuid(), "W-001", "Worker", organizationalDepartmentId: oldDepartment.Id);
        db.AddRange(factory, oldDepartment, newDepartment, worker);
        await db.SaveChangesAsync();
        publisher.Changes.Clear();

        worker.AssignOrganizationalDepartment(newDepartment.Id);
        await db.SaveChangesAsync();

        var change = Assert.Single(publisher.Changes);
        Assert.Equal(ManufacturingEntityType.Worker, change.EntityType);
        Assert.Equal(ManufacturingChangeType.RelationshipChanged, change.ChangeType);
        Assert.Equal(worker.Id, change.WorkerId);
        Assert.Equal(new[] { oldDepartment.Id, newDepartment.Id }.OrderBy(id => id), change.DepartmentIds?.OrderBy(id => id));
        Assert.Contains("department-assignment", change.WorkerChangeKinds ?? []);
        Assert.Equal(["manufacturing:employees"], ManufacturingRealtimeGroups.ForChange(change));
        Assert.Equal("manufacturing.worker-department.changed", ManufacturingDataChangedMessage.From(change).EventType);
    }

    [Fact]
    public async Task Daily_production_approval_cancellation_publishes_one_updated_contextual_event()
    {
        var publisher = new RecordingPublisher();
        var actor = Guid.NewGuid();
        await using var db = CreateDb(publisher, actor);
        var factory = new Factory(Guid.NewGuid(), "Factory", "F-001");
        var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1, "L-001");
        var model = new ProductModel(Guid.NewGuid(), "M-001", "Model");
        var date = new DateOnly(2026, 7, 21);
        var order = new ProductionOrder(Guid.NewGuid(), "DLY-001", model.Id, line.Id, date, 10m, null, actor, DateTime.UtcNow);
        order.MarkDailyOperation("DailyProductionOperations/test", DateTime.UtcNow);
        db.AddRange(factory, line, model, order);
        await db.SaveChangesAsync();
        publisher.Changes.Clear();

        order.ApproveDay(actor, DateTime.UtcNow);
        await db.SaveChangesAsync();
        publisher.Changes.Clear();

        order.ReopenDailyOperationAfterApprovalCancellation(actor, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var change = Assert.Single(publisher.Changes);
        Assert.Equal(ManufacturingEntityType.ProductionOrder, change.EntityType);
        Assert.Equal(ManufacturingChangeType.Updated, change.ChangeType);
        Assert.Equal(factory.Id, change.FactoryId);
        Assert.Equal(line.Id, change.ProductionLineId);
        Assert.Equal(model.Id, change.ProductModelId);
        Assert.Equal(date, change.ProductionDate);
    }

    [Fact]
    public async Task Permanent_assignment_create_and_cancellation_publish_one_contextual_event_after_each_successful_save()
    {
        var publisher = new RecordingPublisher();
        var actor = Guid.NewGuid();
        await using var db = CreateDb(publisher, actor);
        var factory = new Factory(Guid.NewGuid(), "Factory", "F-001");
        var department = new Department(Guid.NewGuid(), factory.Id, "OPS", "التشغيل", "Operations", 1);
        var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1, "L-001", departmentId: department.Id);
        var mainStage = new MainStage(Guid.NewGuid(), department.Id, "Main stage", 1);
        var subStage = new SubStage(Guid.NewGuid(), mainStage.Id, "Sub stage", "S-001", 1, 1, departmentId: mainStage.DepartmentId);
        var worker = new Worker(Guid.NewGuid(), "W-001", "Worker");
        db.AddRange(factory, department, line, mainStage, subStage, worker);
        await db.SaveChangesAsync();
        publisher.Changes.Clear();
        db.ChangeTracker.Clear();

        var assignment = new WorkerDefaultAssignment(Guid.NewGuid(), worker.Id, subStage.Id, actor, DateTime.UtcNow, productionLineId: line.Id);
        db.WorkerDefaultAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var created = Assert.Single(publisher.Changes);
        Assert.Equal(ManufacturingEntityType.WorkerDefaultAssignment, created.EntityType);
        Assert.Equal(ManufacturingChangeType.PermanentAssignmentCreated, created.ChangeType);
        Assert.Equal(assignment.Id, created.EntityId);
        Assert.Equal(worker.Id, created.WorkerId);
        Assert.Equal(factory.Id, created.FactoryId);
        Assert.Equal(line.Id, created.ProductionLineId);
        Assert.Equal(mainStage.Id, created.MainStageId);
        Assert.Equal(subStage.Id, created.SubStageId);
        Assert.Equal(
            ["manufacturing:line-staffing", "manufacturing:daily-production-operations", "manufacturing:manufacturing-command-center", "manufacturing:factory-readiness"],
            ManufacturingRealtimeGroups.ForChange(created));
        var browserMessage = ManufacturingDataChangedMessage.From(created);
        Assert.Equal("manufacturing.data.changed", browserMessage.EventType);
        Assert.Equal("WorkerDefaultAssignment", browserMessage.EntityType);
        Assert.Equal("permanent-assignment-created", browserMessage.ChangeType);
        Assert.Equal(worker.Id, browserMessage.WorkerId);

        publisher.Changes.Clear();
        assignment.Deactivate(DateTime.UtcNow);
        await db.SaveChangesAsync();

        var cancelled = Assert.Single(publisher.Changes);
        Assert.Equal(ManufacturingChangeType.PermanentAssignmentCancelled, cancelled.ChangeType);
        Assert.Equal(assignment.Id, cancelled.EntityId);
        Assert.Equal(worker.Id, cancelled.WorkerId);
        Assert.Equal(factory.Id, cancelled.FactoryId);
        Assert.Equal(line.Id, cancelled.ProductionLineId);
        Assert.Equal(mainStage.Id, cancelled.MainStageId);
        Assert.Equal(subStage.Id, cancelled.SubStageId);
    }

    [Fact]
    public async Task Failed_permanent_assignment_save_publishes_no_event()
    {
        var publisher = new RecordingPublisher();
        var actor = Guid.NewGuid();
        var coordinator = new ManufacturingDataChangeTransactionCoordinator(publisher, NullLogger<ManufacturingDataChangeTransactionCoordinator>.Instance);
        var realtimeInterceptor = new ManufacturingDataChangeSaveChangesInterceptor(
            publisher,
            new CurrentUserStub(actor),
            new CorrelationStub(),
            coordinator,
            NullLogger<ManufacturingDataChangeSaveChangesInterceptor>.Instance);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(realtimeInterceptor, new ThrowOnSavingPermanentAssignmentInterceptor())
            .Options;
        await using var db = new AppDbContext(options);
        db.WorkerDefaultAssignments.Add(new WorkerDefaultAssignment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), actor, DateTime.UtcNow, productionLineId: Guid.NewGuid()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        Assert.Empty(publisher.Changes);
    }

    [Fact]
    public async Task Conflicting_permanent_assignment_save_publishes_no_event()
    {
        var publisher = new RecordingPublisher();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        connection.CreateCollation("SQL_Latin1_General_CP1_CI_AS", (left, right) => string.Compare(left, right, StringComparison.OrdinalIgnoreCase));
        await connection.OpenAsync();
        await using var db = CreateRelationalDb(connection, publisher);
        await db.Database.EnsureCreatedAsync();
        var actor = Guid.NewGuid();
        var factory = new Factory(Guid.NewGuid(), "Factory", "F-001");
        var department = new Department(Guid.NewGuid(), factory.Id, "OPS", "التشغيل", "Operations", 1);
        var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1, "L-001", departmentId: department.Id);
        var mainStage = new MainStage(Guid.NewGuid(), department.Id, "Main stage", 1);
        var subStage = new SubStage(Guid.NewGuid(), mainStage.Id, "Sub stage", "S-001", 1, 1, departmentId: mainStage.DepartmentId);
        var worker = new Worker(Guid.NewGuid(), "W-001", "Worker");
        db.AddRange(factory, department, line, mainStage, subStage, worker);
        await db.SaveChangesAsync();
        publisher.Changes.Clear();

        db.WorkerDefaultAssignments.Add(new WorkerDefaultAssignment(Guid.NewGuid(), worker.Id, subStage.Id, actor, DateTime.UtcNow, productionLineId: line.Id));
        await db.SaveChangesAsync();
        publisher.Changes.Clear();

        db.WorkerDefaultAssignments.Add(new WorkerDefaultAssignment(Guid.NewGuid(), worker.Id, subStage.Id, actor, DateTime.UtcNow, productionLineId: line.Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.Empty(publisher.Changes);
    }

    [Fact]
    public async Task Update_activation_and_relationship_changes_are_classified_without_entity_payloads()
    {
        var publisher = new RecordingPublisher();
        await using var db = CreateDb(publisher, Guid.NewGuid());
        var model = new ProductModel(Guid.NewGuid(), "M-001", "Model");
        db.ProductModels.Add(model);
        await db.SaveChangesAsync();
        publisher.Changes.Clear();

        model.Deactivate();
        await db.SaveChangesAsync();

        var deactivated = Assert.Single(publisher.Changes);
        Assert.Equal(ManufacturingChangeType.Deactivated, deactivated.ChangeType);
        Assert.DoesNotContain(typeof(ManufacturingDataChanged).GetProperties(), property => property.Name is "Name" or "Description" or "Code");
    }

    [Fact]
    public async Task Failed_realtime_publish_does_not_undo_a_successful_database_save()
    {
        await using var db = CreateDb(new RecordingPublisher(throwOnPublish: true), Guid.NewGuid());
        var model = new ProductModel(Guid.NewGuid(), "M-001", "Model");

        db.ProductModels.Add(model);
        await db.SaveChangesAsync();

        Assert.True(await db.ProductModels.AnyAsync(item => item.Id == model.Id));
    }

    [Fact]
    public async Task Failed_explicit_transaction_commit_does_not_publish_a_saved_change()
    {
        var publisher = new RecordingPublisher();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        connection.CreateCollation("SQL_Latin1_General_CP1_CI_AS", (left, right) => string.Compare(left, right, StringComparison.OrdinalIgnoreCase));
        await connection.OpenAsync();
        await using (var setup = CreateRelationalDb(connection, publisher))
        {
            await setup.Database.EnsureCreatedAsync();
        }
        await using var db = CreateRelationalDb(connection, publisher, new ThrowOnCommitInterceptor());

        await using var transaction = await db.Database.BeginTransactionAsync();
        db.ProductModels.Add(new ProductModel(Guid.NewGuid(), "M-ROLLBACK", "Rollback model"));
        await db.SaveChangesAsync();

        Assert.Empty(publisher.Changes);
        Assert.Throws<InvalidOperationException>(() => transaction.Commit());
        Assert.Empty(publisher.Changes);
    }

    [Fact]
    public async Task Successful_explicit_transaction_commit_publishes_each_change_once()
    {
        var publisher = new RecordingPublisher();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        connection.CreateCollation("SQL_Latin1_General_CP1_CI_AS", (left, right) => string.Compare(left, right, StringComparison.OrdinalIgnoreCase));
        await connection.OpenAsync();
        await using var db = CreateRelationalDb(connection, publisher);
        await db.Database.EnsureCreatedAsync();

        await using var transaction = await db.Database.BeginTransactionAsync();
        var model = new ProductModel(Guid.NewGuid(), "M-COMMIT", "Committed model");
        db.ProductModels.Add(model);
        await db.SaveChangesAsync();
        Assert.Empty(publisher.Changes);

        await transaction.CommitAsync();

        var change = Assert.Single(publisher.Changes);
        Assert.Equal(model.Id, change.EntityId);
    }

    [Fact]
    public void Groups_are_limited_to_affected_manufacturing_screens()
    {
        var modelGroups = ManufacturingRealtimeGroups.ForChange(new ManufacturingDataChanged(
            Guid.NewGuid(), ManufacturingEntityType.ProductModel, ManufacturingChangeType.Updated, Guid.NewGuid(), DateTime.UtcNow, null, null));
        var departmentGroups = ManufacturingRealtimeGroups.ForChange(new ManufacturingDataChanged(
            Guid.NewGuid(), ManufacturingEntityType.Department, ManufacturingChangeType.Updated, Guid.NewGuid(), DateTime.UtcNow, null, null));
        var workerGroups = ManufacturingRealtimeGroups.ForChange(new ManufacturingDataChanged(
            Guid.NewGuid(), ManufacturingEntityType.Worker, ManufacturingChangeType.Updated, Guid.NewGuid(), DateTime.UtcNow, null, null));
        var dailyGroups = ManufacturingRealtimeGroups.ForChange(new ManufacturingDataChanged(
            Guid.NewGuid(), ManufacturingEntityType.ProductionOrder, ManufacturingChangeType.Updated, Guid.NewGuid(), DateTime.UtcNow, null, null,
            ProductionLineId: Guid.NewGuid(), ProductModelId: Guid.NewGuid(), ProductionDate: new DateOnly(2026, 7, 21)));
        var attendanceGroups = ManufacturingRealtimeGroups.ForChange(new ManufacturingDataChanged(
            Guid.NewGuid(), ManufacturingEntityType.AttendanceRecord, ManufacturingChangeType.Updated, Guid.NewGuid(), DateTime.UtcNow, null, null));
        var stageRecordGroups = ManufacturingRealtimeGroups.ForChange(new ManufacturingDataChanged(
            Guid.NewGuid(), ManufacturingEntityType.StageProductionRecord, ManufacturingChangeType.Updated, Guid.NewGuid(), DateTime.UtcNow, null, null));

        Assert.Equal(["manufacturing:models", "manufacturing:manufacturing-command-center", "manufacturing:factory-readiness"], modelGroups);
        Assert.Equal(["manufacturing:factory-structure", "manufacturing:departments", "manufacturing:stages", "manufacturing:manufacturing-command-center", "manufacturing:factory-readiness"], departmentGroups);
        Assert.Equal(["manufacturing:employees", "manufacturing:attendance-workforce", "manufacturing:line-staffing", "manufacturing:daily-production-operations", "manufacturing:manufacturing-command-center", "manufacturing:factory-readiness"], workerGroups);
        Assert.Equal(["manufacturing:daily-production-operations", "manufacturing:manufacturing-command-center", "manufacturing:reports"], dailyGroups);
        Assert.Equal(["manufacturing:attendance-workforce", "manufacturing:daily-production-operations", "manufacturing:manufacturing-command-center", "manufacturing:factory-readiness"], attendanceGroups);
        Assert.Equal(["manufacturing:daily-production-operations", "manufacturing:manufacturing-command-center", "manufacturing:reports"], stageRecordGroups);
        Assert.True(ManufacturingRealtimeGroups.TryGetRequiredPermission("models", out var permission));
        Assert.Equal("models.view", permission);
        Assert.True(ManufacturingRealtimeGroups.TryGetRequiredPermission("employees", out permission));
        Assert.Equal("workers.view", permission);
        Assert.True(ManufacturingRealtimeGroups.TryGetRequiredPermission("daily-production-operations", out permission));
        Assert.Equal("production.record", permission);
        Assert.True(ManufacturingRealtimeGroups.TryGetRequiredPermission("line-staffing", out permission));
        Assert.Equal("assignments.view", permission);
        Assert.True(ManufacturingRealtimeGroups.TryGetRequiredPermission("manufacturing-command-center", out permission));
        Assert.Equal("production.view", permission);
        Assert.True(ManufacturingRealtimeGroups.TryGetRequiredPermission("reports", out permission));
        Assert.Equal("reports.production.view", permission);
        Assert.True(ManufacturingRealtimeGroups.TryGetRequiredPermission("attendance-workforce", out permission));
        Assert.Equal("attendance.view", permission);
        Assert.Equal(
            ["factory-structure.view", "stages.view", "assignments.view", "attendance.view"],
            ManufacturingRealtimeGroups.RequiredPermissions("factory-readiness"));
    }

    private static AppDbContext CreateDb(IManufacturingDataChangePublisher publisher, Guid actorUserId)
    {
        var interceptor = new ManufacturingDataChangeSaveChangesInterceptor(
            publisher,
            new CurrentUserStub(actorUserId),
            new CorrelationStub(),
            new ManufacturingDataChangeTransactionCoordinator(publisher, NullLogger<ManufacturingDataChangeTransactionCoordinator>.Instance),
            NullLogger<ManufacturingDataChangeSaveChangesInterceptor>.Instance);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;
        return new AppDbContext(options);
    }

    private static AppDbContext CreateRelationalDb(SqliteConnection connection, IManufacturingDataChangePublisher publisher, params IInterceptor[] additionalInterceptors)
    {
        var coordinator = new ManufacturingDataChangeTransactionCoordinator(publisher, NullLogger<ManufacturingDataChangeTransactionCoordinator>.Instance);
        var interceptors = new IInterceptor[]
        {
            new ManufacturingDataChangeSaveChangesInterceptor(publisher, new CurrentUserStub(Guid.NewGuid()), new CorrelationStub(), coordinator, NullLogger<ManufacturingDataChangeSaveChangesInterceptor>.Instance),
            new ManufacturingDataChangeTransactionInterceptor(coordinator)
        }.Concat(additionalInterceptors).ToArray();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptors)
            .Options;
        return new AppDbContext(options);
    }

    private sealed class RecordingPublisher(bool throwOnPublish = false) : IManufacturingDataChangePublisher
    {
        public List<ManufacturingDataChanged> Changes { get; } = [];

        public Task PublishAsync(ManufacturingDataChanged change, CancellationToken cancellationToken = default)
        {
            if (throwOnPublish) throw new InvalidOperationException("SignalR unavailable");
            Changes.Add(change);
            return Task.CompletedTask;
        }
    }

    private sealed class CurrentUserStub(Guid userId) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public string? UserName => null;
        public bool IsAuthenticated => true;
        public IReadOnlyCollection<string> Roles => [];
    }

    private sealed class CorrelationStub : IManufacturingRealtimeCorrelationContext
    {
        public string? CorrelationId => null;
    }

    private sealed class ThrowOnCommitInterceptor : DbTransactionInterceptor
    {
        public override InterceptionResult TransactionCommitting(System.Data.Common.DbTransaction transaction, TransactionEventData eventData, InterceptionResult result) =>
            throw new InvalidOperationException("Commit failed.");
    }

    private sealed class ThrowOnSavingPermanentAssignmentInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (eventData.Context?.ChangeTracker.Entries<WorkerDefaultAssignment>().Any(entry => entry.State == EntityState.Added) == true)
                throw new InvalidOperationException("Simulated permanent-assignment failure.");
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<WorkerDefaultAssignment>().Any(entry => entry.State == EntityState.Added) == true)
                throw new InvalidOperationException("Simulated permanent-assignment failure.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowOnSavingWorkerInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated worker persistence failure.");
    }
}
